using System.Security.Cryptography;
using Crm.Domain.Common;

namespace Crm.Domain.Accounts;

public enum NoteVisibility { Public, Restricted, Private }

/// <summary>
/// Free-text note on an account. Visibility: Public (anyone who may read the account's notes), Restricted (only users
/// with the restricted-note permission) or Private (author only). Attachments are documents linked to the note.
/// </summary>
public sealed class AccountNote : Entity
{
    public AccountNote(Guid id, string companyId, Guid customerId, string title, string body, NoteVisibility visibility, Guid authorUserId) : base(id)
    {
        CompanyId = companyId;
        CustomerId = customerId;
        AuthorUserId = authorUserId;
        Apply(title, body, visibility);
    }

    private AccountNote() : base(Guid.Empty)
    {
        CompanyId = Title = Body = "EF";
    }

    public string CompanyId { get; private set; }
    public Guid CustomerId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Body { get; private set; } = string.Empty;
    public NoteVisibility Visibility { get; private set; }
    public Guid AuthorUserId { get; private set; }
    public bool IsDeleted { get; private set; }

    public void Update(string title, string body, NoteVisibility visibility, Guid userId)
    {
        if (userId != AuthorUserId) throw new UnauthorizedAccessException("فقط نویسنده می‌تواند یادداشت را ویرایش کند.");
        Apply(title, body, visibility);
        Touch();
    }

    public void Delete(Guid userId)
    {
        if (userId != AuthorUserId) throw new UnauthorizedAccessException("فقط نویسنده می‌تواند یادداشت را حذف کند.");
        IsDeleted = true;
        Touch();
    }

    public void ReassignCustomer(Guid customerId)
    {
        CustomerId = customerId;
        Touch();
    }

    private void Apply(string title, string body, NoteVisibility visibility)
    {
        Title = CrmActivity.Text(title, 200) ?? throw new InvalidOperationException("عنوان یادداشت الزامی است.");
        Body = CrmActivity.Text(body, 8000) ?? throw new InvalidOperationException("متن یادداشت الزامی است.");
        Visibility = Enum.IsDefined(visibility) ? visibility : throw new InvalidOperationException("سطح دسترسی معتبر نیست.");
    }
}

/// <summary>
/// A stored document. It lives independently of accounts and is attached to them through <see cref="DocumentLink"/>,
/// so unlinking never deletes the file and the same contract or catalogue can be linked to several accounts.
/// Bytes are kept in <see cref="DocumentContent"/> so listing documents never loads file contents.
/// </summary>
public sealed class CrmDocument : Entity
{
    public const long MaxBytes = 10 * 1024 * 1024;

    public static readonly IReadOnlyDictionary<string, string> AllowedTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf", [".png"] = "image/png", [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".webp"] = "image/webp",
        [".txt"] = "text/plain", [".csv"] = "text/csv",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
    };

    public CrmDocument(Guid id, string companyId, string title, string fileName, long sizeBytes, string sha256, bool isSensitive,
        Guid uploadedByUserId, Guid? noteId) : base(id)
    {
        CompanyId = companyId;
        var name = Path.GetFileName(fileName ?? string.Empty).Trim();
        if (name.Length is 0 or > 200) throw new InvalidOperationException("نام فایل معتبر نیست.");
        var extension = Path.GetExtension(name);
        ContentType = AllowedTypes.TryGetValue(extension, out var type) ? type : throw new InvalidOperationException("نوع فایل مجاز نیست (PDF، تصویر، Word، Excel، متن).");
        if (sizeBytes is <= 0 or > MaxBytes) throw new InvalidOperationException("حجم فایل باید حداکثر ۱۰ مگابایت باشد.");
        FileName = name;
        Title = CrmActivity.Text(title, 200) ?? Path.GetFileNameWithoutExtension(name);
        SizeBytes = sizeBytes;
        Sha256 = sha256;
        IsSensitive = isSensitive;
        UploadedByUserId = uploadedByUserId;
        NoteId = noteId;
    }

    private CrmDocument() : base(Guid.Empty)
    {
        CompanyId = Title = FileName = ContentType = Sha256 = "EF";
    }

    public string CompanyId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string FileName { get; private set; }
    public string ContentType { get; private set; }
    public long SizeBytes { get; private set; }
    public string Sha256 { get; private set; }
    public bool IsSensitive { get; private set; }
    public Guid UploadedByUserId { get; private set; }
    /// <summary>Set when the document is an attachment of a note (inherits the note's visibility).</summary>
    public Guid? NoteId { get; private set; }
    public bool IsDeleted { get; private set; }

    public static string Hash(byte[] content) => Convert.ToHexString(SHA256.HashData(content));

    /// <summary>A document can only be deleted once it is no longer linked anywhere (unlinking is the normal action).</summary>
    public void Delete(int remainingLinks)
    {
        if (remainingLinks > 0) throw new InvalidOperationException("سند هنوز به حساب دیگری متصل است؛ ابتدا ارتباط‌ها را قطع کنید.");
        IsDeleted = true;
        Touch();
    }
}

public sealed class DocumentContent : Entity
{
    public DocumentContent(Guid documentId, byte[] bytes) : base(documentId) => Bytes = bytes;
    private DocumentContent() : base(Guid.Empty) => Bytes = [];
    public byte[] Bytes { get; private set; }
}

/// <summary>Many-to-many link between a document and an account.</summary>
public sealed class DocumentLink : Entity
{
    public DocumentLink(Guid id, Guid documentId, string companyId, Guid customerId, Guid linkedByUserId) : base(id)
    {
        DocumentId = documentId;
        CompanyId = companyId;
        CustomerId = customerId;
        LinkedByUserId = linkedByUserId;
    }

    private DocumentLink() : base(Guid.Empty) => CompanyId = "EF";

    public Guid DocumentId { get; private set; }
    public string CompanyId { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid LinkedByUserId { get; private set; }

    public void ReassignCustomer(Guid customerId)
    {
        CustomerId = customerId;
        Touch();
    }
}
