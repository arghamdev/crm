using Crm.Application.Abstractions;
using Crm.Application.Contracts;
using Crm.Domain.Accounts;
using Crm.Domain.Common;
using Crm.Domain.Customers;
using static Crm.Application.Services.AccountPermissions;

namespace Crm.Application.Services;

public interface IAccountNoteService
{
    IReadOnlyList<NoteDto> GetNotes(Guid userId, OrganizationSelection organization, Guid accountId, string? query = null, int take = 50);
    NoteDto SaveNote(Guid userId, OrganizationSelection organization, Guid accountId, Guid? noteId, SaveNoteCommand command, IReadOnlyList<FileUpload> attachments,
        DateTimeOffset nowUtc);
    void DeleteNote(Guid userId, OrganizationSelection organization, Guid accountId, Guid noteId, DateTimeOffset nowUtc);
    Guid UploadDocument(Guid userId, OrganizationSelection organization, Guid accountId, string? title, bool sensitive, FileUpload file, Guid operationId,
        DateTimeOffset nowUtc);
    IReadOnlyList<AccountLinkOption> DocumentLinkOptions(Guid userId, OrganizationSelection organization, Guid accountId, string? query);
    void LinkDocument(Guid userId, OrganizationSelection organization, Guid accountId, Guid documentId, DateTimeOffset nowUtc);
    void UnlinkDocument(Guid userId, OrganizationSelection organization, Guid accountId, Guid documentId, DateTimeOffset nowUtc);
    void DeleteDocument(Guid userId, OrganizationSelection organization, Guid accountId, Guid documentId, DateTimeOffset nowUtc);
    (string FileName, string ContentType, byte[] Content) Download(Guid userId, OrganizationSelection organization, Guid accountId, Guid documentId);
}

/// <summary>Notes and documents of accounts. Documents are shared files linked to accounts (unlinking never deletes the file).</summary>
public sealed class AccountNoteService(ICrmDataStore store, IAccessSnapshotService access) : IAccountNoteService
{
    public IReadOnlyList<NoteDto> GetNotes(Guid userId, OrganizationSelection organization, Guid accountId, string? query = null, int take = 50)
    {
        var snapshot = AccountGuard.Snapshot(access, userId);
        return store.Read(data =>
        {
            var account = AccountGuard.Account(data, snapshot, organization, accountId, NoteRead);
            var term = PersianText.NormalizeLetters(query);
            var notes = VisibleNotes(data, snapshot, account, userId)
                .Where(x => term is null || x.Title.Contains(term, StringComparison.OrdinalIgnoreCase) || x.Body.Contains(term, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.CreatedAtUtc).Take(take).ToList();
            var ids = notes.Select(x => (Guid?)x.Id).ToArray();
            var attachments = ids.Length == 0 ? [] : data.Find<CrmDocument>(x => ids.Contains(x.NoteId) && !x.IsDeleted);
            var authors = AccountGuard.UserNames(data, notes.Select(x => x.AuthorUserId));
            var canSeeSensitive = AccountGuard.Allows(snapshot, account, DocumentSensitive);
            return notes.Select(x => new NoteDto(x.Id, x.Title, x.Body, x.Visibility, authors.GetValueOrDefault(x.AuthorUserId, "—"), TehranTime.Format(x.CreatedAtUtc),
                x.AuthorUserId == userId && account.Status != CustomerStatus.Inactive,
                attachments.Where(a => a.NoteId == x.Id && (!a.IsSensitive || canSeeSensitive || a.UploadedByUserId == userId)).Select(a => (a.Id, a.FileName)).ToList())).ToList();
        });
    }

    /// <summary>Public notes for note readers, restricted ones for holders of Note.Restricted.Read, private ones for their author only.</summary>
    public static List<AccountNote> VisibleNotes(CrmDataSet data, AccessSnapshot snapshot, Customer account, Guid userId)
    {
        var restricted = AccountGuard.Allows(snapshot, account, NoteRestricted);
        return data.Find<AccountNote>(x => x.CustomerId == account.Id && !x.IsDeleted).Where(x => x.AuthorUserId == userId ||
            x.Visibility == NoteVisibility.Public || x.Visibility == NoteVisibility.Restricted && restricted).ToList();
    }

    public NoteDto SaveNote(Guid userId, OrganizationSelection organization, Guid accountId, Guid? noteId, SaveNoteCommand command, IReadOnlyList<FileUpload> attachments,
        DateTimeOffset nowUtc)
    {
        var snapshot = AccountGuard.Snapshot(access, userId);
        if (attachments.Count > 5) throw new InvalidOperationException("حداکثر ۵ پیوست برای هر یادداشت مجاز است.");
        var id = store.Write(data =>
        {
            if (noteId is null && ClientOperations.Existing(data, userId, command.OperationId) is { } replayed) return replayed;
            var account = AccountGuard.Account(data, snapshot, organization, accountId, NoteCreate);
            AccountGuard.EnsureMutable(account);
            if (attachments.Count > 0 && !AccountGuard.Allows(snapshot, account, DocumentManage))
                throw new UnauthorizedAccessException("افزودن پیوست نیازمند مجوز Document.Manage است.");
            AccountNote note;
            if (noteId is { } existing)
            {
                note = data.Find<AccountNote>(x => x.Id == existing && x.CustomerId == account.Id && !x.IsDeleted).SingleOrDefault() ?? throw new KeyNotFoundException("یادداشت پیدا نشد.");
                note.Update(command.Title ?? string.Empty, command.Body ?? string.Empty, command.Visibility, userId);
                AccountGuard.Log(data, account, CustomerTimelineType.Note, "یادداشت ویرایش شد", note.Title, userId, nowUtc, note.Id.ToString("N"));
            }
            else
            {
                note = new AccountNote(Guid.NewGuid(), account.CompanyId, account.Id, command.Title ?? string.Empty, command.Body ?? string.Empty, command.Visibility, userId);
                data.AccountNotes.Add(note);
                ClientOperations.Record(data, userId, command.OperationId, "Note", note.Id);
                AccountGuard.Log(data, account, CustomerTimelineType.Note, "یادداشت ثبت شد",
                    note.Visibility == NoteVisibility.Private ? "(یادداشت خصوصی)" : note.Title, userId, nowUtc, note.Id.ToString("N"));
            }
            foreach (var file in attachments) AddDocument(data, account, file.FileName, null, false, file, userId, note.Id, nowUtc); // access follows the note's visibility
            return note.Id;
        });
        return GetNotes(userId, organization, accountId, take: 500).Single(x => x.Id == id);
    }

    public void DeleteNote(Guid userId, OrganizationSelection organization, Guid accountId, Guid noteId, DateTimeOffset nowUtc)
    {
        var snapshot = AccountGuard.Snapshot(access, userId);
        store.Write(data =>
        {
            var account = AccountGuard.Account(data, snapshot, organization, accountId, NoteCreate);
            var note = data.Find<AccountNote>(x => x.Id == noteId && x.CustomerId == account.Id && !x.IsDeleted).SingleOrDefault() ?? throw new KeyNotFoundException("یادداشت پیدا نشد.");
            note.Delete(userId);
            AccountGuard.Log(data, account, CustomerTimelineType.Note, "یادداشت حذف شد", note.Visibility == NoteVisibility.Private ? null : note.Title, userId, nowUtc, note.Id.ToString("N"));
            return true;
        });
    }

    public Guid UploadDocument(Guid userId, OrganizationSelection organization, Guid accountId, string? title, bool sensitive, FileUpload file, Guid operationId,
        DateTimeOffset nowUtc)
    {
        var snapshot = AccountGuard.Snapshot(access, userId);
        return store.Write(data =>
        {
            if (ClientOperations.Existing(data, userId, operationId) is { } replayed) return replayed;
            var account = AccountGuard.Account(data, snapshot, organization, accountId, DocumentManage);
            AccountGuard.EnsureMutable(account);
            var document = AddDocument(data, account, file.FileName, title, sensitive, file, userId, null, nowUtc);
            ClientOperations.Record(data, userId, operationId, "Document", document.Id);
            return document.Id;
        });
    }

    /// <summary>Existing documents that can be linked: documents of other accounts the user can read, or the user's own uploads.</summary>
    public IReadOnlyList<AccountLinkOption> DocumentLinkOptions(Guid userId, OrganizationSelection organization, Guid accountId, string? query)
    {
        var snapshot = AccountGuard.Snapshot(access, userId);
        return store.Read(data =>
        {
            var account = AccountGuard.Account(data, snapshot, organization, accountId, DocumentManage);
            var term = PersianText.NormalizeLetters(query);
            var linkedHere = data.Find<DocumentLink>(x => x.CustomerId == account.Id).Select(x => x.DocumentId).ToHashSet();
            var candidates = data.Find<CrmDocument>(x => x.CompanyId == account.CompanyId && !x.IsDeleted && x.NoteId == null &&
                (term == null || x.Title.Contains(term) || x.FileName.Contains(term))).Where(x => !linkedHere.Contains(x.Id)).Take(200).ToList();
            var ids = candidates.Select(x => x.Id).ToArray();
            var links = ids.Length == 0 ? [] : data.Find<DocumentLink>(x => ids.Contains(x.DocumentId));
            var accountIds = links.Select(x => x.CustomerId).Distinct().ToArray();
            var readable = accountIds.Length == 0 ? [] : data.Find<Customer>(x => accountIds.Contains(x.Id))
                .Where(x => AccountGuard.InContext(snapshot, organization, "Customer.Read", x) && AccountGuard.Allows(snapshot, x, DocumentRead))
                .ToDictionary(x => x.Id, x => x.Name);
            var sensitive = AccountGuard.Allows(snapshot, account, DocumentSensitive);
            return candidates.Where(x => (!x.IsSensitive || sensitive) &&
                    (x.UploadedByUserId == userId || links.Any(l => l.DocumentId == x.Id && readable.ContainsKey(l.CustomerId))))
                .OrderByDescending(x => x.CreatedAtUtc).Take(30)
                .Select(x => new AccountLinkOption(x.Id, x.Title, $"{x.FileName} · {string.Join("، ", links.Where(l => l.DocumentId == x.Id).Select(l => readable.GetValueOrDefault(l.CustomerId)).Where(n => n is not null).Take(2))}"))
                .ToList();
        });
    }

    public void LinkDocument(Guid userId, OrganizationSelection organization, Guid accountId, Guid documentId, DateTimeOffset nowUtc)
    {
        if (!DocumentLinkOptions(userId, organization, accountId, null).Any(x => x.Id == documentId) &&
            !DocumentLinkOptionsById(userId, organization, accountId, documentId))
            throw new UnauthorizedAccessException("این سند برای اتصال در دسترس شما نیست.");
        var snapshot = AccountGuard.Snapshot(access, userId);
        store.Write(data =>
        {
            var account = AccountGuard.Account(data, snapshot, organization, accountId, DocumentManage);
            AccountGuard.EnsureMutable(account);
            if (data.Find<DocumentLink>(x => x.DocumentId == documentId && x.CustomerId == account.Id).Any()) return true;
            var document = data.Find<CrmDocument>(x => x.Id == documentId && !x.IsDeleted).Single();
            data.DocumentLinks.Add(new DocumentLink(Guid.NewGuid(), document.Id, account.CompanyId, account.Id, userId));
            AccountGuard.Log(data, account, CustomerTimelineType.Document, "سند موجود متصل شد", document.Title, userId, nowUtc, document.Id.ToString("N"));
            return true;
        });
    }

    private bool DocumentLinkOptionsById(Guid userId, OrganizationSelection organization, Guid accountId, Guid documentId) =>
        DocumentLinkOptions(userId, organization, accountId, store.Read(data => data.Find<CrmDocument>(x => x.Id == documentId).Select(x => x.Title).FirstOrDefault()))
            .Any(x => x.Id == documentId);

    /// <summary>Removes only the link between this account and the document; the file and its other links stay.</summary>
    public void UnlinkDocument(Guid userId, OrganizationSelection organization, Guid accountId, Guid documentId, DateTimeOffset nowUtc)
    {
        var snapshot = AccountGuard.Snapshot(access, userId);
        store.Write(data =>
        {
            var account = AccountGuard.Account(data, snapshot, organization, accountId, DocumentManage);
            var link = data.Find<DocumentLink>(x => x.DocumentId == documentId && x.CustomerId == account.Id).SingleOrDefault() ?? throw new KeyNotFoundException("سند به این حساب متصل نیست.");
            var document = data.Find<CrmDocument>(x => x.Id == documentId).Single();
            if (document.NoteId is not null) throw new InvalidOperationException("پیوست یادداشت از طریق خود یادداشت مدیریت می‌شود.");
            data.DocumentLinks.Remove(link);
            AccountGuard.Log(data, account, CustomerTimelineType.Document, "ارتباط سند قطع شد", document.Title, userId, nowUtc, document.Id.ToString("N"));
            return true;
        });
    }

    public void DeleteDocument(Guid userId, OrganizationSelection organization, Guid accountId, Guid documentId, DateTimeOffset nowUtc)
    {
        var snapshot = AccountGuard.Snapshot(access, userId);
        store.Write(data =>
        {
            var account = AccountGuard.Account(data, snapshot, organization, accountId, DocumentManage);
            var link = data.Find<DocumentLink>(x => x.DocumentId == documentId && x.CustomerId == account.Id).SingleOrDefault() ?? throw new KeyNotFoundException("سند به این حساب متصل نیست.");
            var document = data.Find<CrmDocument>(x => x.Id == documentId && !x.IsDeleted).Single();
            if (document.UploadedByUserId != userId && !AccountGuard.ManagerWide(snapshot, account.CompanyId))
                throw new UnauthorizedAccessException("فقط بارگذاری‌کننده یا مدیر فروش می‌تواند سند را حذف کند.");
            var others = data.Find<DocumentLink>(x => x.DocumentId == documentId && x.CustomerId != account.Id).Count;
            document.Delete(others);
            data.DocumentLinks.Remove(link);
            AccountGuard.Log(data, account, CustomerTimelineType.Document, "سند حذف شد", document.Title, userId, nowUtc, document.Id.ToString("N"));
            return true;
        });
    }

    public (string FileName, string ContentType, byte[] Content) Download(Guid userId, OrganizationSelection organization, Guid accountId, Guid documentId)
    {
        var snapshot = AccountGuard.Snapshot(access, userId);
        return store.Read(data =>
        {
            var account = AccountGuard.Account(data, snapshot, organization, accountId, DocumentRead);
            var document = data.Find<CrmDocument>(x => x.Id == documentId && !x.IsDeleted).SingleOrDefault() ?? throw new KeyNotFoundException("سند پیدا نشد.");
            var linked = data.Find<DocumentLink>(x => x.DocumentId == documentId && x.CustomerId == account.Id).Any();
            if (!linked) throw new KeyNotFoundException("سند به این حساب متصل نیست.");
            if (document.IsSensitive && document.UploadedByUserId != userId && !AccountGuard.Allows(snapshot, account, DocumentSensitive))
                throw new UnauthorizedAccessException("مشاهدهٔ سند محرمانه مجاز نیست.");
            if (document.NoteId is { } noteId && !VisibleNotes(data, snapshot, account, userId).Any(x => x.Id == noteId))
                throw new UnauthorizedAccessException("این پیوست متعلق به یادداشتی است که برای شما قابل مشاهده نیست.");
            var content = data.Find<DocumentContent>(x => x.Id == documentId).Single();
            return (document.FileName, document.ContentType, content.Bytes);
        });
    }

    private static CrmDocument AddDocument(CrmDataSet data, Customer account, string fileName, string? title, bool sensitive, FileUpload file, Guid userId, Guid? noteId,
        DateTimeOffset nowUtc)
    {
        var document = new CrmDocument(Guid.NewGuid(), account.CompanyId, title ?? string.Empty, file.FileName, file.Content.LongLength, CrmDocument.Hash(file.Content),
            sensitive, userId, noteId);
        data.CrmDocuments.Add(document);
        data.Append(new DocumentContent(document.Id, file.Content));
        data.DocumentLinks.Add(new DocumentLink(Guid.NewGuid(), document.Id, account.CompanyId, account.Id, userId));
        AccountGuard.Log(data, account, CustomerTimelineType.Document, noteId is null ? "سند بارگذاری شد" : "پیوست یادداشت بارگذاری شد",
            document.IsSensitive ? $"{document.Title} (محرمانه)" : document.Title, userId, nowUtc, document.Id.ToString("N"));
        return document;
    }
}
