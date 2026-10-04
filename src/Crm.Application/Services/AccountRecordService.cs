using Crm.Application.Abstractions;
using Crm.Application.Contracts;
using Crm.Domain.Accounts;
using Crm.Domain.Channel;
using Crm.Domain.Common;
using Crm.Domain.Customers;
using Crm.Domain.Sales;
using static Crm.Application.Services.AccountPermissions;

namespace Crm.Application.Services;

public interface IAccountRecordService
{
    AccountRecordFormDto GetCreateForm(Guid userId, OrganizationSelection organization, Guid accountId, string key,
        IReadOnlyDictionary<string, string?>? values, DateTimeOffset nowUtc);
    Guid Create(Guid userId, OrganizationSelection organization, Guid accountId, string key, AccountRecordCommand command, DateTimeOffset nowUtc);
    AccountLinkFormDto GetLinkForm(Guid userId, OrganizationSelection organization, Guid accountId, string key, string? query, DateTimeOffset nowUtc);
    void Link(Guid userId, OrganizationSelection organization, Guid accountId, string key, Guid recordId, AccountRecordCommand command, DateTimeOffset nowUtc);
    void Act(Guid userId, OrganizationSelection organization, Guid accountId, string key, Guid recordId, string action, AccountRecordCommand command,
        DateTimeOffset nowUtc);
    AccountRecordFormDto GetAccountForm(Guid userId, OrganizationSelection organization, Guid accountId, string form, IReadOnlyDictionary<string, string?>? values);
    void UpdateAccount(Guid userId, OrganizationSelection organization, Guid accountId, string form, AccountRecordCommand command, DateTimeOffset nowUtc);
}

/// <summary>
/// Write side of the related-records sections of the account file: create from the account (account preselected),
/// link an existing record, unlink (never deletes the record) and the status actions each module allows.
/// Every operation re-checks scope and the section's own permission on the server.
/// </summary>
public sealed class AccountRecordService(ICrmDataStore store, IAccessSnapshotService access, ISalesPipelineService sales, IAccountNoteService notes)
    : IAccountRecordService
{
    public const string StatusChange = "Customer.Status.Change";
    private static readonly (string, string)[] Currencies = [("IRR", "ریال (IRR)"), ("USD", "دلار (USD)"), ("EUR", "یورو (EUR)"), ("AED", "درهم (AED)"), ("CNY", "یوان (CNY)")];

    private static AccountSectionMeta Meta(string key) =>
        AccountFileService.Sections.SingleOrDefault(x => x.Key == key) ?? throw new KeyNotFoundException("بخش پیدا نشد.");

    // ───────────────────────────── create forms ─────────────────────────────

    public AccountRecordFormDto GetCreateForm(Guid userId, OrganizationSelection organization, Guid accountId, string key,
        IReadOnlyDictionary<string, string?>? values, DateTimeOffset nowUtc)
    {
        var meta = Meta(key);
        var snapshot = AccountGuard.Snapshot(access, userId);
        return store.Read(data =>
        {
            var account = AccountGuard.Account(data, snapshot, organization, accountId, meta.ReadPermission);
            if (meta.CreatePermission is not { } create || !AccountGuard.Allows(snapshot, account, create))
                throw new UnauthorizedAccessException("ایجاد در این بخش مجاز نیست.");
            AccountGuard.EnsureMutable(account);
            var v = new FormValues(values);
            var today = JalaliDate.Format(TehranTime.Today(nowUtc));
            var options = AccountActivityService.Options(data, snapshot, account, userId, nowUtc);
            var contacts = Choice(options.Contacts, "— بدون رابط —");
            var users = options.Users.Select(x => (x.Id.ToString(), x.Name)).ToList();
            string action = $"/customers/{account.Id}/records/{key}/new";
            AccountRecordFormDto Form(string title, string submit, IReadOnlyList<AccountFormField> fields, string? note = null, bool multipart = false) =>
                new(account.Id, account.Name, key, title, submit, action, fields, multipart, note, v.Operation);
            return key switch
            {
                "opportunities" => Form("ایجاد فرصت فروش", "ثبت فرصت",
                [
                    new("title", "عنوان فرصت", "text", true, v["title"], Wide: true, Placeholder: "مثلاً تمدید قرارداد پشتیبانی ۱۴۰۶"),
                    new("contactId", "فرد رابط", "select", false, v["contactId"] ?? options.Contacts.Select(x => x.Id.ToString()).FirstOrDefault(), contacts),
                    new("stage", "مرحله", "select", true, v["stage"] ?? nameof(OpportunityStage.Identified),
                        Enum.GetValues<OpportunityStage>().Where(x => x is not (OpportunityStage.Won or OpportunityStage.Lost))
                            .Select(x => (x.ToString(), AccountFileService.StageLabel(x))).ToList()),
                    new("value", "مبلغ برآوردی", "money", true, v["value"], Ltr: true),
                    new("currency", "واحد پول", "select", true, v["currency"] ?? "IRR", Currencies),
                    new("probability", "احتمال موفقیت (٪)", "number", false, v["probability"], Help: "خالی = احتمال پیش‌فرض مرحله", Ltr: true),
                    new("expectedClose", "تاریخ پیش‌بینی بسته‌شدن", "date", true, v["expectedClose"] ?? JalaliDate.Format(TehranTime.Today(nowUtc).AddMonths(1))),
                    new("ownerUserId", "مسئول", "select", true, v["ownerUserId"] ?? userId.ToString(), users),
                    new("nextAction", "اقدام بعدی", "text", true, v["nextAction"], Wide: true, Placeholder: "مثلاً ارسال معرفی راهکار"),
                    new("nextActionDate", "تاریخ اقدام بعدی", "date", true, v["nextActionDate"] ?? JalaliDate.Format(TehranTime.Today(nowUtc).AddDays(2))),
                    new("nextActionTime", "ساعت اقدام بعدی", "time", false, v["nextActionTime"] ?? "10:00", Ltr: true)
                ], "فرصت مستقیماً به همین حساب و شعبهٔ آن متصل می‌شود."),
                "leads" => Form("ایجاد سرنخ برای این حساب", "ثبت سرنخ",
                [
                    new("name", "عنوان سرنخ / نیاز", "text", true, v["name"], Wide: true, Placeholder: "مثلاً درخواست خط تولید جدید"),
                    new("contactId", "فرد رابط", "select", false, v["contactId"], contacts, "اگر رابط انتخاب شود، نام و راه ارتباطی او استفاده می‌شود."),
                    new("contactName", "نام تماس (اگر رابط ثبت نشده)", "text", false, v["contactName"]),
                    new("phone", "تلفن", "text", false, v["phone"], Ltr: true),
                    new("email", "ایمیل", "text", false, v["email"], Ltr: true),
                    new("source", "منبع", "select", true, v["source"] ?? "مشتری فعلی",
                        new[] { "مشتری فعلی", "تماس ورودی", "وب‌سایت", "نمایشگاه", "معرفی", "کمپین", "سایر" }.Select(x => (x, x)).ToList()),
                    new("ownerUserId", "مسئول پیگیری", "select", true, v["ownerUserId"] ?? userId.ToString(), users)
                ], "سرنخ به همین حساب متصل می‌ماند؛ پس از احراز می‌تواند به فرصت تبدیل شود."),
                "payments" => Form("ثبت پرداخت / دریافت", "ثبت",
                [
                    new("direction", "نوع", "select", true, v["direction"] ?? nameof(PaymentDirection.Receipt),
                        [(nameof(PaymentDirection.Receipt), "دریافت از حساب"), (nameof(PaymentDirection.Payment), "پرداخت به حساب")]),
                    new("method", "روش", "select", true, v["method"] ?? nameof(PaymentMethod.BankTransfer),
                        Enum.GetValues<PaymentMethod>().Select(x => (x.ToString(), AccountFileService.MethodLabel(x))).ToList()),
                    new("amount", "مبلغ", "money", true, v["amount"], Ltr: true),
                    new("currency", "واحد پول", "select", true, v["currency"] ?? "IRR", Currencies),
                    new("paidOn", "تاریخ", "date", true, v["paidOn"] ?? today),
                    new("reference", "شماره چک / کد پیگیری", "text", false, v["reference"], Help: "برای چک و حواله الزامی است.", Ltr: true),
                    new("invoiceReference", "شماره فاکتور مرتبط", "text", false, v["invoiceReference"], Ltr: true),
                    new("description", "شرح", "textarea", false, v["description"], Wide: true)
                ], "ثبت با وضعیت «ثبت‌شده» انجام می‌شود و تا تأیید کاربر دیگری در جمع‌ها حساب نمی‌شود."),
                "guarantees" => Form("ثبت تضمین / ودیعه", "ثبت",
                [
                    new("type", "نوع", "select", true, v["type"] ?? nameof(DealerGuaranteeType.Cheque),
                        Enum.GetValues<DealerGuaranteeType>().Select(x => (x.ToString(), AccountFileService.GuaranteeLabel(x))).ToList()),
                    new("number", "شماره", "text", true, v["number"], Ltr: true),
                    new("issuer", "بانک / صادرکننده", "text", false, v["issuer"]),
                    new("amount", "مبلغ (ریال)", "money", true, v["amount"], Ltr: true),
                    new("issuedOn", "تاریخ صدور", "date", true, v["issuedOn"] ?? today),
                    new("expiresOn", "تاریخ سررسید", "date", false, v["expiresOn"]),
                    new("notes", "توضیح", "textarea", false, v["notes"], Wide: true)
                ], "سی روز پیش از سررسید برای مسئولان تضمین اعلان ارسال می‌شود."),
                "bankAccounts" => Form("افزودن حساب بانکی", "ثبت",
                [
                    new("bankName", "بانک", "text", true, v["bankName"]),
                    new("iban", "شماره شبا", "text", true, v["iban"], Placeholder: "IR000000000000000000000000", Ltr: true),
                    new("accountNumber", "شماره حساب", "text", false, v["accountNumber"], Ltr: true),
                    new("holderName", "صاحب حساب", "text", true, v["holderName"] ?? account.Name),
                    new("isPrimary", "حساب اصلی", "checkbox", false, v["isPrimary"])
                ]),
                "salesContracts" or "serviceContracts" => ContractForm(data, snapshot, account, userId, key, v, today, Form),
                "projects" => Form("ایجاد پروژه", "ثبت پروژه",
                [
                    new("name", "نام پروژه", "text", true, v["name"], Wide: true),
                    new("code", "کد پروژه", "text", false, v["code"], Help: "خالی = تولید خودکار", Ltr: true),
                    new("startOn", "شروع", "date", true, v["startOn"] ?? today),
                    new("endOn", "پایان", "date", false, v["endOn"]),
                    new("managerUserId", "مدیر پروژه", "select", false, v["managerUserId"] ?? userId.ToString(), users),
                    new("opportunityId", "فرصت مرتبط", "select", false, v["opportunityId"], Choice(options.Opportunities, "—")),
                    new("contractId", "قرارداد مرتبط", "select", false, v["contractId"], Choice(options.Contracts, "—")),
                    new("budget", "بودجه", "money", false, v["budget"], Ltr: true),
                    new("currency", "واحد پول", "select", true, v["currency"] ?? "IRR", Currencies)
                ]),
                "participations" => Form("ثبت مشارکت", "ثبت",
                [
                    new("kind", "نوع", "select", true, v["kind"] ?? nameof(ParticipationKind.Event),
                        Enum.GetValues<ParticipationKind>().Select(x => (x.ToString(), AccountFileService.ParticipationKindLabel(x))).ToList()),
                    new("title", "عنوان رویداد / برنامه", "text", true, v["title"], Wide: true),
                    new("role", "نقش حساب", "text", false, v["role"], Placeholder: "مثلاً حامی، غرفه‌دار، شریک اجرایی"),
                    new("startOn", "شروع", "date", true, v["startOn"] ?? today),
                    new("endOn", "پایان", "date", false, v["endOn"]),
                    new("notes", "توضیح", "textarea", false, v["notes"], Wide: true)
                ], "مشارکت یعنی حضور یا نقش حساب در رویداد، برنامه یا همکاری تجاری."),
                "reservations" or "gifts" or "samples" => Form(key switch { "reservations" => "ثبت رزرو", "gifts" => "ثبت هدیه", _ => "ثبت نمونه محصول" }, "ثبت",
                [
                    new("itemName", "کالا / خدمت", "text", true, v["itemName"], Wide: true),
                    new("itemCode", "کد کالا", "text", false, v["itemCode"], Ltr: true),
                    new("quantity", "مقدار", "number", true, v["quantity"] ?? "1", Ltr: true),
                    new("date", "تاریخ", "date", true, v["date"] ?? today),
                    new("notes", "توضیح", "textarea", false, v["notes"], Wide: true)
                ]),
                "campaigns" => Form("کمپین جدید و افزودن این حساب", "ثبت کمپین",
                [
                    new("name", "نام کمپین", "text", true, v["name"], Wide: true),
                    new("type", "نوع", "select", true, v["type"] ?? nameof(CampaignType.Event),
                        Enum.GetValues<CampaignType>().Select(x => (x.ToString(), AccountFileService.CampaignTypeLabel(x))).ToList()),
                    new("startOn", "شروع", "date", true, v["startOn"] ?? today),
                    new("endOn", "پایان", "date", false, v["endOn"]),
                    new("contactId", "رابط هدف", "select", false, v["contactId"], contacts)
                ]),
                "targetLists" => Form("لیست هدف جدید و افزودن این حساب", "ثبت لیست",
                [
                    new("name", "نام لیست", "text", true, v["name"], Wide: true),
                    new("description", "توضیح", "textarea", false, v["description"], Wide: true)
                ]),
                "surveys" => Form("ثبت پاسخ نظرسنجی", "ثبت پاسخ",
                [
                    new("surveyId", "نظرسنجی", "select", false, v["surveyId"],
                        Choice(data.Find<Survey>(x => x.CompanyId == account.CompanyId && x.IsActive).OrderBy(x => x.Title)
                            .Select(x => (x.Id, $"{x.Title} ({(x.Kind == SurveyKind.Nps ? "NPS ۰ تا ۱۰" : "رضایت ۱ تا ۵")})")).ToList(), "— نظرسنجی جدید —")),
                    new("newSurveyTitle", "عنوان نظرسنجی جدید", "text", false, v["newSurveyTitle"], Help: "فقط وقتی نظرسنجی انتخاب نشده"),
                    new("newSurveyKind", "نوع نظرسنجی جدید", "select", false, v["newSurveyKind"] ?? nameof(SurveyKind.Csat),
                        [(nameof(SurveyKind.Csat), "رضایت (۱ تا ۵)"), (nameof(SurveyKind.Nps), "NPS (۰ تا ۱۰)")]),
                    new("contactId", "پاسخ‌دهنده", "select", false, v["contactId"], contacts),
                    new("score", "امتیاز", "number", true, v["score"], Ltr: true),
                    new("respondedOn", "تاریخ پاسخ", "date", true, v["respondedOn"] ?? today),
                    new("comment", "نظر", "textarea", false, v["comment"], Wide: true)
                ]),
                "documents" => Form("بارگذاری سند", "بارگذاری",
                [
                    new("title", "عنوان سند", "text", false, v["title"], Help: "خالی = نام فایل", Wide: true),
                    new("file", "فایل", "file", true, null, Help: "PDF، تصویر، Word، Excel، CSV یا متن؛ حداکثر ۱۰ مگابایت", Wide: true),
                    new("sensitive", "محرمانه (فقط دارندگان مجوز اسناد محرمانه)", "checkbox", false, v["sensitive"])
                ], multipart: true),
                _ => throw new KeyNotFoundException("ایجاد از پرونده برای این بخش پشتیبانی نمی‌شود.")
            };
        });
    }

    private static AccountRecordFormDto ContractForm(CrmDataSet data, AccessSnapshot snapshot, Customer account, Guid userId, string key, FormValues v, string today,
        Func<string, string, IReadOnlyList<AccountFormField>, string?, bool, AccountRecordFormDto> form)
    {
        if (key == "serviceContracts")
            return form("ایجاد قرارداد خدمات", "ثبت قرارداد",
            [
                new("title", "عنوان قرارداد", "text", true, v["title"], Wide: true),
                new("number", "شماره قرارداد", "text", false, v["number"], Help: "خالی = تولید خودکار", Ltr: true),
                new("startOn", "شروع", "date", true, v["startOn"] ?? today),
                new("endOn", "پایان", "date", true, v["endOn"]),
                new("serviceLevel", "سطح خدمت", "text", false, v["serviceLevel"], Placeholder: "مثلاً طلایی / پاسخ ۴ ساعته"),
                new("amount", "مبلغ", "money", false, v["amount"], Ltr: true),
                new("currency", "واحد پول", "select", true, v["currency"] ?? "IRR", Currencies)
            ], null, false);
        var won = WonOpportunities(data, snapshot, account, userId);
        var selected = v["opportunityId"] is { } id && won.Any(x => x.Id.ToString() == id) ? won.Single(x => x.Id.ToString() == id) : won.FirstOrDefault();
        return form("ایجاد قرارداد فروش", "ثبت قرارداد",
        [
            new("opportunityId", "فرصت برنده‌شده", "select", true, selected?.Id.ToString(), won.Select(x => (x.Id.ToString(), $"{x.Code} · {x.Title}")).ToList(),
                won.Count == 0 ? "این حساب فرصت برنده‌شده‌ای ندارد؛ قرارداد فروش فقط از فرصت برنده ایجاد می‌شود." : null, Wide: true),
            new("title", "عنوان قرارداد", "text", true, v["title"] ?? selected?.Title, Wide: true),
            new("number", "شماره قرارداد", "text", false, v["number"], Help: "خالی = تولید خودکار", Ltr: true),
            new("startOn", "شروع", "date", true, v["startOn"] ?? today),
            new("endOn", "پایان", "date", true, v["endOn"]),
            new("amount", "مبلغ", "money", false, v["amount"] ?? selected?.Value.ToString("0"), Ltr: true),
            new("currency", "واحد پول", "select", true, v["currency"] ?? selected?.CurrencyCode ?? "IRR", Currencies)
        ], "مطابق قواعد فروش، قرارداد فروش فقط از فرصت برنده‌شدهٔ همین حساب ساخته می‌شود.", false);
    }

    private static List<Opportunity> WonOpportunities(CrmDataSet data, AccessSnapshot snapshot, Customer account, Guid userId)
    {
        var won = data.Find<Opportunity>(x => x.CustomerId == account.Id && x.Stage == OpportunityStage.Won);
        return (AccountGuard.ManagerWide(snapshot, account.CompanyId) ? won : won.Where(x => x.OwnerUserId == userId)).OrderByDescending(x => x.UpdatedAtUtc).ToList();
    }

    private static List<(string Value, string Label)> Choice(IEnumerable<(Guid Id, string Name)> items, string empty) =>
        new[] { (string.Empty, empty) }.Concat(items.Select(x => (x.Id.ToString(), x.Name))).ToList();

    // ───────────────────────────── create ─────────────────────────────

    public Guid Create(Guid userId, OrganizationSelection organization, Guid accountId, string key, AccountRecordCommand command, DateTimeOffset nowUtc)
    {
        var meta = Meta(key);
        var snapshot = AccountGuard.Snapshot(access, userId);
        switch (key)
        {
            case "opportunities": return CreateOpportunity(userId, organization, accountId, command, snapshot, nowUtc);
            case "leads": return CreateLead(userId, organization, accountId, command, snapshot, nowUtc);
            case "documents":
                var file = command.Files?.FirstOrDefault() ?? throw new InvalidOperationException("فایل را انتخاب کنید.");
                return notes.UploadDocument(userId, organization, accountId, command.Get("title"), Flag(command.Get("sensitive")), file, command.OperationId, nowUtc);
        }
        return store.Write(data =>
        {
            if (ClientOperations.Existing(data, userId, command.OperationId) is { } replayed) return replayed;
            var account = AccountGuard.Account(data, snapshot, organization, accountId, meta.ReadPermission);
            if (meta.CreatePermission is not { } permission || !AccountGuard.Allows(snapshot, account, permission))
                throw new UnauthorizedAccessException("ایجاد در این بخش مجاز نیست.");
            AccountGuard.EnsureMutable(account);
            var today = TehranTime.Today(nowUtc);
            var c = command;
            Guid id = Guid.NewGuid();
            string title;
            switch (key)
            {
                case "payments":
                {
                    var payment = new AccountPayment(id, account.CompanyId, account.Id, Parse<PaymentDirection>(c.Get("direction"), "نوع"), Money(c.Get("amount"), "مبلغ"),
                        c.Get("currency") ?? "IRR", Date(c.Get("paidOn"), "تاریخ"), Parse<PaymentMethod>(c.Get("method"), "روش"), c.Get("reference"),
                        c.Get("invoiceReference"), c.Get("description"), userId, today);
                    data.Append(payment);
                    title = $"{(payment.Direction == PaymentDirection.Receipt ? "دریافت" : "پرداخت")} {payment.Amount:N0} {payment.CurrencyCode} ثبت شد";
                    AccountGuard.Log(data, account, CustomerTimelineType.Payment, title, "وضعیت: ثبت‌شده (در انتظار تأیید)", userId, nowUtc, payment.Reference);
                    break;
                }
                case "guarantees":
                {
                    var guarantee = DealerGuarantee.ForAccount(id, account.Id, account.CompanyId, account.BranchId, account.TerritoryId,
                        Parse<DealerGuaranteeType>(c.Get("type"), "نوع"), c.Get("number") ?? throw new InvalidOperationException("شماره الزامی است."), c.Get("issuer"),
                        Money(c.Get("amount"), "مبلغ"), Date(c.Get("issuedOn"), "تاریخ صدور"), OptionalDate(c.Get("expiresOn"), "سررسید"), c.Get("notes"), userId);
                    data.Append(guarantee);
                    AccountGuard.Log(data, account, CustomerTimelineType.RelationChanged, $"تضمین {AccountFileService.GuaranteeLabel(guarantee.Type)} {guarantee.Number} ثبت شد",
                        $"{guarantee.Amount:N0} IRR", userId, nowUtc);
                    break;
                }
                case "bankAccounts":
                {
                    var primary = Flag(c.Get("isPrimary"));
                    var existing = data.Find<AccountBankAccount>(x => x.CustomerId == account.Id && x.IsActive);
                    var normalized = IranianIdentifiers.NormalizeIban(c.Get("iban"));
                    if (existing.Any(x => x.Iban == normalized)) throw new InvalidOperationException("این شماره شبا قبلاً برای حساب ثبت شده است.");
                    if (primary) foreach (var other in existing.Where(x => x.IsPrimary)) other.SetPrimary(false);
                    var bank = new AccountBankAccount(id, account.CompanyId, account.Id, c.Get("bankName") ?? "", c.Get("iban") ?? "", c.Get("accountNumber"),
                        c.Get("holderName") ?? "", primary || existing.Count == 0);
                    data.Append(bank);
                    AccountGuard.Log(data, account, CustomerTimelineType.RelationChanged, $"حساب بانکی {bank.BankName} افزوده شد", bank.Iban, userId, nowUtc);
                    break;
                }
                case "salesContracts":
                case "serviceContracts":
                {
                    var kind = key == "salesContracts" ? ContractKind.Sales : ContractKind.Service;
                    Opportunity? opportunity = null;
                    if (kind == ContractKind.Sales)
                    {
                        var opportunityId = Guid.TryParse(c.Get("opportunityId"), out var o) ? o : throw new InvalidOperationException("فرصت برنده‌شده را انتخاب کنید.");
                        opportunity = WonOpportunities(data, snapshot, account, userId).SingleOrDefault(x => x.Id == opportunityId)
                            ?? throw new InvalidOperationException("قرارداد فروش فقط از فرصت برنده‌شدهٔ همین حساب ایجاد می‌شود.");
                    }
                    var number = c.Get("number") ?? RecordCodes.Next(data.Find<AccountContract>(x => x.CompanyId == account.CompanyId).Select(x => x.Number),
                        kind == ContractKind.Sales ? $"SC-{JalaliDate.YearMonth(nowUtc).Year}-" : $"SV-{JalaliDate.YearMonth(nowUtc).Year}-", 1, 3);
                    if (data.Find<AccountContract>(x => x.CompanyId == account.CompanyId && x.Number == number).Any())
                        throw new InvalidOperationException("شمارهٔ قرارداد تکراری است.");
                    var contract = new AccountContract(id, account.CompanyId, account.Id, kind, number, c.Get("title") ?? "", Date(c.Get("startOn"), "شروع"),
                        Date(c.Get("endOn"), "پایان"), OptionalMoney(c.Get("amount"), "مبلغ"), c.Get("currency") ?? opportunity?.CurrencyCode ?? "IRR", opportunity?.Id,
                        c.Get("serviceLevel"), userId);
                    data.Append(contract);
                    AccountGuard.Log(data, account, CustomerTimelineType.Contract, $"قرارداد {contract.Number} ایجاد شد",
                        opportunity is null ? contract.Title : $"{contract.Title} · از فرصت {opportunity.Code}", userId, nowUtc, contract.Number);
                    break;
                }
                case "projects":
                {
                    var options = AccountActivityService.Options(data, snapshot, account, userId, nowUtc);
                    var manager = OptionalGuid(c.Get("managerUserId"));
                    if (manager is { } m && options.Users.All(x => x.Id != m)) throw new InvalidOperationException("مدیر پروژه در دامنهٔ این حساب نیست.");
                    var opportunityId = OptionalGuid(c.Get("opportunityId"));
                    if (opportunityId is { } op && !data.Find<Opportunity>(x => x.Id == op && x.CustomerId == account.Id).Any())
                        throw new InvalidOperationException("فرصت انتخاب‌شده به این حساب تعلق ندارد.");
                    var contractId = OptionalGuid(c.Get("contractId"));
                    if (contractId is { } ct && !data.Find<AccountContract>(x => x.Id == ct && x.CustomerId == account.Id).Any())
                        throw new InvalidOperationException("قرارداد انتخاب‌شده به این حساب تعلق ندارد.");
                    var code = c.Get("code") ?? RecordCodes.Next(data.Find<AccountProject>(x => x.CompanyId == account.CompanyId).Select(x => x.Code),
                        $"PRJ-{JalaliDate.YearMonth(nowUtc).Year}-", 1, 3);
                    if (data.Find<AccountProject>(x => x.CompanyId == account.CompanyId && x.Code == code).Any()) throw new InvalidOperationException("کد پروژه تکراری است.");
                    var project = new AccountProject(id, account.CompanyId, account.Id, code, c.Get("name") ?? "", Date(c.Get("startOn"), "شروع"),
                        OptionalDate(c.Get("endOn"), "پایان"), manager, opportunityId, contractId, OptionalMoney(c.Get("budget"), "بودجه"), c.Get("currency") ?? "IRR");
                    data.Append(project);
                    AccountGuard.Log(data, account, CustomerTimelineType.Project, $"پروژه {project.Code} ایجاد شد", project.Name, userId, nowUtc, project.Code);
                    break;
                }
                case "participations":
                {
                    var participation = new AccountParticipation(id, account.CompanyId, account.Id, Parse<ParticipationKind>(c.Get("kind"), "نوع"), c.Get("title") ?? "",
                        c.Get("role"), Date(c.Get("startOn"), "شروع"), OptionalDate(c.Get("endOn"), "پایان"), c.Get("notes"));
                    data.Append(participation);
                    AccountGuard.Log(data, account, CustomerTimelineType.Marketing, $"مشارکت در «{participation.Title}» ثبت شد", participation.Role, userId, nowUtc);
                    break;
                }
                case "reservations":
                case "gifts":
                case "samples":
                {
                    var kind = key switch { "reservations" => AllocationKind.Reservation, "gifts" => AllocationKind.Gift, _ => AllocationKind.Sample };
                    var quantity = decimal.TryParse(PersianText.Normalize(c.Get("quantity")), System.Globalization.NumberStyles.Number,
                        System.Globalization.CultureInfo.InvariantCulture, out var q) ? q : throw new InvalidOperationException("مقدار را عددی وارد کنید.");
                    var allocation = new AccountAllocation(id, account.CompanyId, account.Id, kind, c.Get("itemCode"), c.Get("itemName") ?? "", quantity,
                        Date(c.Get("date"), "تاریخ"), c.Get("notes"));
                    data.Append(allocation);
                    AccountGuard.Log(data, account, CustomerTimelineType.RelationChanged, $"{meta.Title}: {allocation.ItemName} ثبت شد", $"مقدار {allocation.Quantity:0.###}",
                        userId, nowUtc);
                    break;
                }
                case "campaigns":
                {
                    var campaign = new Campaign(Guid.NewGuid(), account.CompanyId, c.Get("name") ?? "", Parse<CampaignType>(c.Get("type"), "نوع"), Date(c.Get("startOn"), "شروع"),
                        OptionalDate(c.Get("endOn"), "پایان"), userId);
                    data.Append(campaign);
                    var contactId = OptionalContact(data, account, c.Get("contactId"));
                    data.Append(new CampaignMember(id, campaign.Id, account.CompanyId, account.Id, contactId, userId));
                    AccountGuard.Log(data, account, CustomerTimelineType.Marketing, $"عضویت در کمپین «{campaign.Name}»", "کمپین جدید از پروندهٔ حساب", userId, nowUtc);
                    break;
                }
                case "targetLists":
                {
                    var list = new TargetList(Guid.NewGuid(), account.CompanyId, c.Get("name") ?? "", c.Get("description"), userId);
                    data.Append(list);
                    data.Append(new TargetListMember(id, list.Id, account.CompanyId, account.Id, userId));
                    AccountGuard.Log(data, account, CustomerTimelineType.Marketing, $"افزوده شد به لیست هدف «{list.Name}»", null, userId, nowUtc);
                    break;
                }
                case "surveys":
                {
                    Survey survey;
                    if (OptionalGuid(c.Get("surveyId")) is { } surveyId)
                        survey = data.Find<Survey>(x => x.Id == surveyId && x.CompanyId == account.CompanyId).SingleOrDefault() ?? throw new InvalidOperationException("نظرسنجی پیدا نشد.");
                    else
                    {
                        survey = new Survey(Guid.NewGuid(), account.CompanyId, c.Get("newSurveyTitle") ?? throw new InvalidOperationException("نظرسنجی را انتخاب کنید یا عنوان نظرسنجی جدید را وارد کنید."),
                            Parse<SurveyKind>(c.Get("newSurveyKind"), "نوع نظرسنجی"), userId);
                        data.Append(survey);
                    }
                    var score = int.TryParse(PersianText.Normalize(c.Get("score")), out var s) ? s : throw new InvalidOperationException("امتیاز را عددی وارد کنید.");
                    var response = new SurveyResponse(id, survey, account.Id, OptionalContact(data, account, c.Get("contactId")), score, c.Get("comment"),
                        Date(c.Get("respondedOn"), "تاریخ پاسخ"), userId, today);
                    data.Append(response);
                    AccountGuard.Log(data, account, CustomerTimelineType.Marketing, $"پاسخ نظرسنجی «{survey.Title}»: امتیاز {score}", response.Comment, userId, nowUtc);
                    break;
                }
                default:
                    throw new KeyNotFoundException("ایجاد از پرونده برای این بخش پشتیبانی نمی‌شود.");
            }
            ClientOperations.Record(data, userId, command.OperationId, key, id);
            return id;
        });
    }

    private Guid CreateOpportunity(Guid userId, OrganizationSelection organization, Guid accountId, AccountRecordCommand c, AccessSnapshot snapshot, DateTimeOffset nowUtc)
    {
        var account = store.Read(data =>
        {
            var found = AccountGuard.Account(data, snapshot, organization, accountId, "Opportunity.Create");
            AccountGuard.EnsureMutable(found);
            return found;
        });
        var expected = TehranTime.ToUtc(c.Get("expectedClose"), "17:00", "تاریخ بسته‌شدن") ?? throw new InvalidOperationException("تاریخ پیش‌بینی بسته‌شدن الزامی است.");
        var next = TehranTime.ToUtc(c.Get("nextActionDate"), c.Get("nextActionTime") ?? "10:00", "تاریخ اقدام بعدی")
                   ?? throw new InvalidOperationException("تاریخ اقدام بعدی الزامی است.");
        if (next < nowUtc.AddMinutes(-5)) throw new InvalidOperationException("زمان اقدام بعدی نمی‌تواند در گذشته باشد.");
        int? probability = c.Get("probability") is { } p ? int.TryParse(PersianText.Normalize(p), out var value) ? value : throw new InvalidOperationException("احتمال را عددی وارد کنید.") : null;
        var result = sales.CreateOpportunity(userId, organization, new CreateOpportunityCommand(account.Id,
            c.Get("title") ?? throw new InvalidOperationException("عنوان فرصت الزامی است."), Money(c.Get("value"), "مبلغ"),
            OptionalGuid(c.Get("ownerUserId")) ?? userId, account.BranchId, account.TerritoryId, expected, "پرونده حساب",
            c.Get("nextAction") ?? throw new InvalidOperationException("اقدام بعدی الزامی است."), next, ContactId: OptionalGuid(c.Get("contactId")),
            CurrencyCode: c.Get("currency") ?? "IRR", InitialStage: Parse<OpportunityStage>(c.Get("stage") ?? nameof(OpportunityStage.Identified), "مرحله"),
            Probability: probability, OperationId: c.OperationId), nowUtc);
        return result.Id;
    }

    private Guid CreateLead(Guid userId, OrganizationSelection organization, Guid accountId, AccountRecordCommand c, AccessSnapshot snapshot, DateTimeOffset nowUtc)
    {
        var (account, contact, canSeeContact) = store.Read(data =>
        {
            var found = AccountGuard.Account(data, snapshot, organization, accountId, "Lead.Create");
            AccountGuard.EnsureMutable(found);
            var contactId = OptionalGuid(c.Get("contactId"));
            var person = contactId is { } id ? data.Find<CustomerContact>(x => x.Id == id && x.CustomerId == found.Id && x.IsActive).SingleOrDefault()
                ?? throw new InvalidOperationException("فرد رابط انتخاب‌شده به این حساب تعلق ندارد.") : null;
            return (found, person, snapshot.PermissionsFor(found.CompanyId).Contains(FieldMasking.ContactPermission));
        });
        // Contact details of an existing contact are copied only for users who may read them (no unmasking through the lead).
        var phone = c.Get("phone") ?? (canSeeContact ? contact?.Mobile ?? contact?.Phone : null);
        var email = c.Get("email") ?? (canSeeContact ? contact?.Email : null);
        var name = c.Get("contactName") ?? contact?.FullName;
        var result = sales.CreateLead(userId, organization, new CreateLeadCommand(c.Get("name") ?? throw new InvalidOperationException("عنوان سرنخ الزامی است."),
            name, c.Get("source") ?? "مشتری فعلی", null, account.BranchId, OptionalGuid(c.Get("ownerUserId")) ?? userId, phone, email, account.TerritoryId,
            CustomerId: account.Id, OperationId: c.OperationId), nowUtc);
        return result.Id;
    }

    // ───────────────────────────── link existing ─────────────────────────────

    public AccountLinkFormDto GetLinkForm(Guid userId, OrganizationSelection organization, Guid accountId, string key, string? query, DateTimeOffset nowUtc)
    {
        var meta = Meta(key);
        var snapshot = AccountGuard.Snapshot(access, userId);
        if (key == "documents")
        {
            var name = store.Read(data => AccountGuard.Account(data, snapshot, organization, accountId, DocumentManage).Name);
            return new AccountLinkFormDto(accountId, name, key, "اتصال سند موجود", $"/customers/{accountId}/records/{key}/link", query,
                notes.DocumentLinkOptions(userId, organization, accountId, query), [], "سند به این حساب هم متصل می‌شود و در حساب‌های دیگر باقی می‌ماند.");
        }
        return store.Read(data =>
        {
            var account = AccountGuard.Account(data, snapshot, organization, accountId, meta.ReadPermission);
            if (meta.LinkPermission is not { } permission || !AccountGuard.Allows(snapshot, account, permission))
                throw new UnauthorizedAccessException("اتصال رکورد در این بخش مجاز نیست.");
            AccountGuard.EnsureMutable(account);
            var term = PersianText.NormalizeLetters(query);
            bool Match(params string?[] values) => term is null || values.Any(x => x?.Contains(term, StringComparison.OrdinalIgnoreCase) == true);
            var action = $"/customers/{account.Id}/records/{key}/link";
            switch (key)
            {
                case "leads":
                {
                    var leads = LinkableLeads(data, snapshot, account, userId).Where(x => Match(x.Name, x.Code, x.Contact, x.Phone)).OrderByDescending(x => x.CreatedAtUtc).Take(30)
                        .Select(x => new AccountLinkOption(x.Id, $"{x.Code} · {x.Name}", $"{x.Contact} · {x.Source} · {x.Owner}")).ToList();
                    return new AccountLinkFormDto(account.Id, account.Name, key, "اتصال سرنخ موجود", action, query, leads, [],
                        "فقط سرنخ‌های باز و بدون حساب در شعبهٔ همین حساب که به آن‌ها دسترسی دارید نمایش داده می‌شوند.");
                }
                case "campaigns":
                {
                    var current = data.Find<CampaignMember>(x => x.CustomerId == account.Id).Select(x => x.CampaignId).ToHashSet();
                    var campaigns = data.Find<Campaign>(x => x.CompanyId == account.CompanyId).Where(x => x.AcceptsMembers && !current.Contains(x.Id) && Match(x.Name))
                        .OrderByDescending(x => x.StartOn).Take(30)
                        .Select(x => new AccountLinkOption(x.Id, x.Name, $"{AccountFileService.CampaignTypeLabel(x.Type)} · {AccountFileService.CampaignStatusLabel(x.Status)} · از {JalaliDate.Format(x.StartOn)}"))
                        .ToList();
                    var contacts = AccountActivityService.Options(data, snapshot, account, userId, nowUtc).Contacts;
                    return new AccountLinkFormDto(account.Id, account.Name, key, "افزودن به کمپین موجود", action, query, campaigns,
                        [new AccountFormField("contactId", "رابط هدف", "select", false, null, Choice(contacts, "— بدون رابط —"))]);
                }
                case "targetLists":
                {
                    var current = data.Find<TargetListMember>(x => x.CustomerId == account.Id).Select(x => x.TargetListId).ToHashSet();
                    var lists = data.Find<TargetList>(x => x.CompanyId == account.CompanyId).Where(x => !current.Contains(x.Id) && Match(x.Name, x.Description))
                        .OrderBy(x => x.Name).Take(30).Select(x => new AccountLinkOption(x.Id, x.Name, x.Description)).ToList();
                    return new AccountLinkFormDto(account.Id, account.Name, key, "افزودن به لیست هدف موجود", action, query, lists, []);
                }
                case "hierarchy":
                {
                    var blocked = Ancestors(data, account).Append(account.Id).ToHashSet();
                    var candidates = data.Find<Customer>(x => x.CompanyId == account.CompanyId && x.Status != CustomerStatus.Inactive && x.Id != account.Id)
                        .Where(x => !blocked.Contains(x.Id) && x.ParentCustomerId != account.Id && Visible(snapshot, x) && Match(x.Name, x.Code))
                        .OrderBy(x => x.Name).Take(30)
                        .Select(x => new AccountLinkOption(x.Id, x.Name, $"{x.Code} · {x.Branch}{(x.ParentCustomerId is null ? "" : " · دارای حساب مادر")}")).ToList();
                    return new AccountLinkFormDto(account.Id, account.Name, key, "حساب مادر / زیرمجموعه", action, query, candidates,
                        [new AccountFormField("role", "نوع ارتباط", "select", true, "child",
                            [("child", "حساب انتخابی زیرمجموعهٔ این حساب شود"), ("parent", "حساب انتخابی مادر این حساب شود")])],
                        "ساختار درختی بدون حلقه نگه داشته می‌شود؛ هر حساب حداکثر یک حساب مادر دارد.");
                }
                default:
                    throw new KeyNotFoundException("اتصال رکورد موجود برای این بخش پشتیبانی نمی‌شود.");
            }
        });
    }

    private static bool Visible(AccessSnapshot snapshot, Customer customer) =>
        snapshot.AllowsRecord(customer.CompanyId, "Customer.Read", customer.BranchId, customer.TerritoryId);

    private static IEnumerable<Lead> LinkableLeads(CrmDataSet data, AccessSnapshot snapshot, Customer account, Guid userId)
    {
        var leads = data.Find<Lead>(x => x.CompanyId == account.CompanyId && x.BranchId == account.BranchId && x.CustomerId == null)
            .Where(x => x.Status is not (LeadStatus.Converted or LeadStatus.Disqualified or LeadStatus.Duplicate or LeadStatus.Invalid));
        return AccountGuard.ManagerWide(snapshot, account.CompanyId) ? leads : leads.Where(x => x.OwnerUserId == userId);
    }

    private static List<Guid> Ancestors(CrmDataSet data, Customer account)
    {
        var result = new List<Guid>();
        var current = account.ParentCustomerId;
        while (current is { } id && !result.Contains(id) && result.Count < 50)
        {
            result.Add(id);
            current = data.Find<Customer>(x => x.Id == id).Select(x => x.ParentCustomerId).FirstOrDefault();
        }
        return result;
    }

    public void Link(Guid userId, OrganizationSelection organization, Guid accountId, string key, Guid recordId, AccountRecordCommand command, DateTimeOffset nowUtc)
    {
        var meta = Meta(key);
        var snapshot = AccountGuard.Snapshot(access, userId);
        if (key == "documents")
        {
            notes.LinkDocument(userId, organization, accountId, recordId, nowUtc);
            return;
        }
        store.Write(data =>
        {
            var account = AccountGuard.Account(data, snapshot, organization, accountId, meta.ReadPermission);
            if (meta.LinkPermission is not { } permission || !AccountGuard.Allows(snapshot, account, permission))
                throw new UnauthorizedAccessException("اتصال رکورد در این بخش مجاز نیست.");
            AccountGuard.EnsureMutable(account);
            switch (key)
            {
                case "leads":
                {
                    var lead = LinkableLeads(data, snapshot, account, userId).SingleOrDefault(x => x.Id == recordId)
                        ?? throw new InvalidOperationException("سرنخ قابل اتصال نیست (بسته، متصل به حساب دیگر یا خارج از دسترسی).");
                    lead.ReassignCustomer(account.Id);
                    AccountGuard.Log(data, account, CustomerTimelineType.RelationChanged, $"سرنخ {lead.Code} به حساب متصل شد", lead.Name, userId, nowUtc, lead.Code);
                    break;
                }
                case "campaigns":
                {
                    var campaign = data.Find<Campaign>(x => x.Id == recordId && x.CompanyId == account.CompanyId).SingleOrDefault() ?? throw new KeyNotFoundException();
                    if (!campaign.AcceptsMembers) throw new InvalidOperationException("کمپین پایان‌یافته یا لغوشده عضو جدید نمی‌پذیرد.");
                    if (data.Find<CampaignMember>(x => x.CampaignId == campaign.Id && x.CustomerId == account.Id).Any())
                        throw new InvalidOperationException("حساب قبلاً عضو این کمپین است.");
                    data.Append(new CampaignMember(Guid.NewGuid(), campaign.Id, account.CompanyId, account.Id, OptionalContact(data, account, command.Get("contactId")), userId));
                    AccountGuard.Log(data, account, CustomerTimelineType.Marketing, $"عضویت در کمپین «{campaign.Name}»", null, userId, nowUtc);
                    break;
                }
                case "targetLists":
                {
                    var list = data.Find<TargetList>(x => x.Id == recordId && x.CompanyId == account.CompanyId).SingleOrDefault() ?? throw new KeyNotFoundException();
                    if (data.Find<TargetListMember>(x => x.TargetListId == list.Id && x.CustomerId == account.Id).Any())
                        throw new InvalidOperationException("حساب قبلاً در این لیست است.");
                    data.Append(new TargetListMember(Guid.NewGuid(), list.Id, account.CompanyId, account.Id, userId));
                    AccountGuard.Log(data, account, CustomerTimelineType.Marketing, $"افزوده شد به لیست هدف «{list.Name}»", null, userId, nowUtc);
                    break;
                }
                case "hierarchy":
                {
                    var other = data.Find<Customer>(x => x.Id == recordId && x.CompanyId == account.CompanyId).SingleOrDefault(x => Visible(snapshot, x))
                        ?? throw new KeyNotFoundException("حساب پیدا نشد.");
                    if (other.Status == CustomerStatus.Inactive) throw new InvalidOperationException("حساب غیرفعال قابل اتصال نیست.");
                    if (!AccountGuard.Allows(snapshot, other, RelationManage)) throw new UnauthorizedAccessException("مدیریت ارتباط حساب انتخابی مجاز نیست.");
                    if (command.Get("role") == "parent")
                    {
                        if (Ancestors(data, other).Contains(account.Id) || other.Id == account.Id) throw new InvalidOperationException("این ارتباط حلقه ایجاد می‌کند.");
                        account.SetParent(other.Id);
                        AccountGuard.Log(data, account, CustomerTimelineType.RelationChanged, $"حساب مادر: {other.Name}", other.Code, userId, nowUtc);
                        AccountGuard.Log(data, other, CustomerTimelineType.RelationChanged, $"زیرمجموعهٔ جدید: {account.Name}", account.Code, userId, nowUtc);
                    }
                    else
                    {
                        if (other.ParentCustomerId is not null) throw new InvalidOperationException("این حساب زیرمجموعهٔ حساب دیگری است؛ ابتدا از آن جدا شود.");
                        if (Ancestors(data, account).Contains(other.Id)) throw new InvalidOperationException("این ارتباط حلقه ایجاد می‌کند.");
                        other.SetParent(account.Id);
                        AccountGuard.Log(data, account, CustomerTimelineType.RelationChanged, $"زیرمجموعهٔ جدید: {other.Name}", other.Code, userId, nowUtc);
                        AccountGuard.Log(data, other, CustomerTimelineType.RelationChanged, $"حساب مادر: {account.Name}", account.Code, userId, nowUtc);
                    }
                    break;
                }
                default:
                    throw new KeyNotFoundException("اتصال رکورد موجود برای این بخش پشتیبانی نمی‌شود.");
            }
            return true;
        });
    }

    // ───────────────────────────── row actions ─────────────────────────────

    public void Act(Guid userId, OrganizationSelection organization, Guid accountId, string key, Guid recordId, string action, AccountRecordCommand command,
        DateTimeOffset nowUtc)
    {
        var meta = Meta(key);
        var snapshot = AccountGuard.Snapshot(access, userId);
        if (key == "documents")
        {
            switch (action)
            {
                case "unlink": notes.UnlinkDocument(userId, organization, accountId, recordId, nowUtc); return;
                case "delete": notes.DeleteDocument(userId, organization, accountId, recordId, nowUtc); return;
                default: throw new KeyNotFoundException();
            }
        }
        store.Write(data =>
        {
            var account = AccountGuard.Account(data, snapshot, organization, accountId, meta.ReadPermission);
            AccountGuard.EnsureMutable(account);
            void Need(string permission)
            {
                if (!AccountGuard.Allows(snapshot, account, permission)) throw new UnauthorizedAccessException($"{permission} permission is required.");
            }
            string Reason(string label) => command.Get("reason") ?? throw new InvalidOperationException($"{label} الزامی است.");
            T Record<T>(T? entity) where T : Crm.Domain.Common.Entity
            {
                if (entity is null) throw new KeyNotFoundException("رکورد پیدا نشد.");
                if (command.Version is { } expected && expected != entity.Version)
                    throw new InvalidOperationException("رکورد هم‌زمان تغییر کرده است؛ فهرست را تازه‌سازی کنید.");
                return entity;
            }
            switch (key, action)
            {
                case ("leads", "unlink"):
                {
                    Need(RelationManage);
                    var all = data.Find<Lead>(x => x.Id == recordId && x.CustomerId == account.Id);
                    var lead = Record((AccountGuard.ManagerWide(snapshot, account.CompanyId) ? all : all.Where(x => x.OwnerUserId == userId)).SingleOrDefault());
                    lead.DetachCustomer();
                    AccountGuard.Log(data, account, CustomerTimelineType.RelationChanged, $"ارتباط سرنخ {lead.Code} با حساب قطع شد", "سرنخ حذف نشد.", userId, nowUtc, lead.Code);
                    break;
                }
                case ("salesContracts" or "serviceContracts", "activate" or "terminate"):
                {
                    Need(ContractManage);
                    var contract = Record(data.Find<AccountContract>(x => x.Id == recordId && x.CustomerId == account.Id).SingleOrDefault());
                    if (action == "activate") contract.Activate(); else contract.Terminate(Reason("دلیل خاتمه"));
                    AccountGuard.Log(data, account, CustomerTimelineType.Contract, $"قرارداد {contract.Number} {(action == "activate" ? "فعال شد" : "خاتمه یافت")}",
                        contract.TerminationReason, userId, nowUtc, contract.Number);
                    break;
                }
                case ("projects", "status"):
                {
                    Need(ProjectManage);
                    var project = Record(data.Find<AccountProject>(x => x.Id == recordId && x.CustomerId == account.Id).SingleOrDefault());
                    project.ChangeStatus(Parse<ProjectStatus>(command.Get("status"), "وضعیت"));
                    AccountGuard.Log(data, account, CustomerTimelineType.Project, $"پروژه {project.Code}: {AccountFileService.ProjectLabel(project.Status)}", null, userId, nowUtc, project.Code);
                    break;
                }
                case ("payments", "approve" or "cancel" or "return"):
                {
                    Need(PaymentApprove);
                    var payment = Record(data.Find<AccountPayment>(x => x.Id == recordId && x.CustomerId == account.Id).SingleOrDefault());
                    if (action == "approve") payment.Approve(userId, nowUtc);
                    else if (action == "cancel") payment.Cancel(userId, Reason("دلیل لغو"), nowUtc);
                    else payment.MarkReturned(userId, Reason("دلیل برگشت"), nowUtc);
                    AccountGuard.Log(data, account, CustomerTimelineType.Payment,
                        $"{(payment.Direction == PaymentDirection.Receipt ? "دریافت" : "پرداخت")} {payment.Amount:N0} {payment.CurrencyCode}: {AccountFileService.PaymentLabel(payment.Status)}",
                        payment.DecisionNote, userId, nowUtc, payment.Reference);
                    break;
                }
                case ("guarantees", "release" or "forfeit"):
                {
                    Need(GuaranteeManage);
                    var guarantee = Record(data.Find<DealerGuarantee>(x => x.Id == recordId && x.CustomerId == account.Id).SingleOrDefault());
                    if (action == "release") guarantee.Release(userId, Reason("دلیل آزادسازی"), nowUtc); else guarantee.Forfeit(userId, Reason("دلیل ضبط"), nowUtc);
                    AccountGuard.Log(data, account, CustomerTimelineType.RelationChanged, $"تضمین {guarantee.Number} {(action == "release" ? "آزاد شد" : "ضبط شد")}",
                        guarantee.DecisionReason, userId, nowUtc);
                    break;
                }
                case ("bankAccounts", "primary" or "deactivate"):
                {
                    Need(BankManage);
                    var accounts = data.Find<AccountBankAccount>(x => x.CustomerId == account.Id && x.IsActive);
                    var bank = Record(accounts.SingleOrDefault(x => x.Id == recordId));
                    if (action == "primary")
                    {
                        foreach (var other in accounts.Where(x => x.IsPrimary && x.Id != bank.Id)) other.SetPrimary(false);
                        bank.SetPrimary(true);
                    }
                    else bank.Deactivate();
                    AccountGuard.Log(data, account, CustomerTimelineType.RelationChanged, $"حساب بانکی {bank.BankName} {(action == "primary" ? "اصلی شد" : "غیرفعال شد")}", bank.Iban,
                        userId, nowUtc);
                    break;
                }
                case ("campaigns", "status"):
                {
                    Need(CampaignManage);
                    var member = Record(data.Find<CampaignMember>(x => x.Id == recordId && x.CustomerId == account.Id).SingleOrDefault());
                    member.ChangeStatus(Parse<CampaignMemberStatus>(command.Get("status"), "وضعیت"));
                    AccountGuard.Log(data, account, CustomerTimelineType.Marketing, $"وضعیت کمپین: {AccountFileService.MemberLabel(member.Status)}", null, userId, nowUtc);
                    break;
                }
                case ("campaigns", "unlink"):
                {
                    Need(CampaignManage);
                    var member = Record(data.CampaignMembers.SingleOrDefault(x => x.Id == recordId && x.CustomerId == account.Id));
                    data.CampaignMembers.Remove(member);
                    AccountGuard.Log(data, account, CustomerTimelineType.Marketing, "خروج از کمپین", "کمپین حذف نشد.", userId, nowUtc);
                    break;
                }
                case ("targetLists", "unlink"):
                {
                    Need(CampaignManage);
                    var member = Record(data.TargetListMembers.SingleOrDefault(x => x.Id == recordId && x.CustomerId == account.Id));
                    data.TargetListMembers.Remove(member);
                    AccountGuard.Log(data, account, CustomerTimelineType.Marketing, "خروج از لیست هدف", "لیست حذف نشد.", userId, nowUtc);
                    break;
                }
                case ("participations", "status"):
                {
                    Need(RelationManage);
                    var participation = Record(data.Find<AccountParticipation>(x => x.Id == recordId && x.CustomerId == account.Id).SingleOrDefault());
                    participation.ChangeStatus(Parse<ParticipationStatus>(command.Get("status"), "وضعیت"));
                    AccountGuard.Log(data, account, CustomerTimelineType.Marketing, $"مشارکت «{participation.Title}»: {AccountFileService.ParticipationLabel(participation.Status)}",
                        null, userId, nowUtc);
                    break;
                }
                case ("reservations" or "gifts" or "samples", "status"):
                {
                    Need(RelationManage);
                    var allocation = Record(data.Find<AccountAllocation>(x => x.Id == recordId && x.CustomerId == account.Id).SingleOrDefault());
                    allocation.ChangeStatus(Parse<AllocationStatus>(command.Get("status"), "وضعیت"));
                    AccountGuard.Log(data, account, CustomerTimelineType.RelationChanged, $"{meta.Title}: {allocation.ItemName} — {AccountFileService.AllocationLabel(allocation.Status)}",
                        null, userId, nowUtc);
                    break;
                }
                case ("hierarchy", "unlink-parent"):
                {
                    Need(RelationManage);
                    Record(account);
                    if (account.ParentCustomerId != recordId) throw new KeyNotFoundException();
                    account.SetParent(null);
                    AccountGuard.Log(data, account, CustomerTimelineType.RelationChanged, "ارتباط با حساب مادر قطع شد", null, userId, nowUtc);
                    break;
                }
                case ("hierarchy", "unlink-child"):
                {
                    Need(RelationManage);
                    var child = Record(data.Find<Customer>(x => x.Id == recordId && x.ParentCustomerId == account.Id).SingleOrDefault(x => Visible(snapshot, x)));
                    child.SetParent(null);
                    AccountGuard.Log(data, account, CustomerTimelineType.RelationChanged, $"زیرمجموعهٔ {child.Name} جدا شد", child.Code, userId, nowUtc);
                    AccountGuard.Log(data, child, CustomerTimelineType.RelationChanged, $"ارتباط با حساب مادر {account.Name} قطع شد", account.Code, userId, nowUtc);
                    break;
                }
                case ("branches" or "serviceCenters" or "salesCenters", "deactivate"):
                {
                    Need("Customer.Update");
                    var site = Record(data.Find<CustomerAddress>(x => x.Id == recordId && x.CustomerId == account.Id && x.IsActive).SingleOrDefault());
                    site.Deactivate();
                    AccountGuard.Log(data, account, CustomerTimelineType.RelationChanged, $"{meta.Title}: «{site.Title}» غیرفعال شد", null, userId, nowUtc);
                    break;
                }
                default:
                    throw new KeyNotFoundException("این عملیات برای این بخش تعریف نشده است.");
            }
            return true;
        });
    }

    // ───────────────────────────── account-level ─────────────────────────────

    public AccountRecordFormDto GetAccountForm(Guid userId, OrganizationSelection organization, Guid accountId, string form, IReadOnlyDictionary<string, string?>? values)
    {
        var snapshot = AccountGuard.Snapshot(access, userId);
        return store.Read(data =>
        {
            var account = AccountGuard.Account(data, snapshot, organization, accountId);
            var v = new FormValues(values);
            var action = $"/customers/{account.Id}/account/{form}";
            var version = new AccountFormField("expectedVersion", "", "hidden", false, account.Version.ToString());
            switch (form)
            {
                case "status":
                {
                    if (!AccountGuard.Allows(snapshot, account, StatusChange)) throw new UnauthorizedAccessException("تغییر وضعیت حساب مجاز نیست.");
                    var deactivate = account.Status != CustomerStatus.Inactive;
                    if (!deactivate && data.Find<CustomerMergeOperation>(x => x.MergedCustomerId == account.Id && x.Status == CustomerMergeStatus.Merged).Any())
                        throw new InvalidOperationException("این حساب در حساب دیگری ادغام شده است؛ فعال‌سازی فقط با بازگردانی ادغام ممکن است.");
                    var open = deactivate ? OpenWork(data, account, nowUtc: DateTimeOffset.UtcNow) : null;
                    return new AccountRecordFormDto(account.Id, account.Name, form, deactivate ? "غیرفعال‌سازی حساب" : "فعال‌سازی حساب", deactivate ? "غیرفعال شود" : "فعال شود",
                        action, [version, new AccountFormField("reason", "دلیل", "textarea", true, v["reason"], Wide: true)], Note: deactivate
                            ? $"حساب حذف نمی‌شود؛ همهٔ سوابق می‌ماند ولی ثبت رکورد جدید برای آن بسته می‌شود. {open}"
                            : "پس از فعال‌سازی، ثبت فعالیت و رکوردهای جدید دوباره ممکن است.");
                }
                case "classify":
                {
                    if (!AccountGuard.Allows(snapshot, account, "Customer.Update")) throw new UnauthorizedAccessException("ویرایش حساب مجاز نیست.");
                    return new AccountRecordFormDto(account.Id, account.Name, form, "نوع رابطه و برچسب‌ها", "ذخیره", action,
                    [
                        version,
                        new("relationship", "نوع رابطه", "select", true, v["relationship"] ?? account.RelationshipType.ToString(),
                            Enum.GetValues<AccountRelationship>().Select(x => (x.ToString(), RelationshipLabel(x))).ToList()),
                        new("tags", "برچسب‌ها", "text", false, v["tags"] ?? string.Join("، ", account.TagList), Help: "با ویرگول جدا کنید؛ حداکثر ۱۰ برچسب", Wide: true)
                    ]);
                }
                default: throw new KeyNotFoundException();
            }
        });
    }

    private static string? OpenWork(CrmDataSet data, Customer account, DateTimeOffset nowUtc)
    {
        var opportunities = data.Find<Opportunity>(x => x.CustomerId == account.Id).Count(x => x.Stage is not (OpportunityStage.Won or OpportunityStage.Lost));
        var activities = data.Find<CrmActivity>(x => x.CustomerId == account.Id && x.Status == ActivityStatus.Planned).Count;
        var payments = data.Find<AccountPayment>(x => x.CustomerId == account.Id && x.Status == PaymentStatus.Registered).Count;
        var parts = new List<string>();
        if (opportunities > 0) parts.Add($"{opportunities} فرصت باز");
        if (activities > 0) parts.Add($"{activities} فعالیت برنامه‌ریزی‌شده");
        if (payments > 0) parts.Add($"{payments} پرداخت در انتظار تأیید");
        return parts.Count == 0 ? null : $"توجه: {string.Join("، ", parts)} دارد.";
    }

    public static string RelationshipLabel(AccountRelationship value) => value switch
    {
        AccountRelationship.Customer => "مشتری", AccountRelationship.Prospect => "مشتری بالقوه", AccountRelationship.Supplier => "تأمین‌کننده", _ => "شریک تجاری"
    };

    public void UpdateAccount(Guid userId, OrganizationSelection organization, Guid accountId, string form, AccountRecordCommand command, DateTimeOffset nowUtc)
    {
        var snapshot = AccountGuard.Snapshot(access, userId);
        store.Write(data =>
        {
            var account = AccountGuard.Account(data, snapshot, organization, accountId);
            if (command.Version is not { } expected || expected != account.Version)
                throw new InvalidOperationException("حساب هم‌زمان تغییر کرده است؛ صفحه را تازه‌سازی کنید.");
            switch (form)
            {
                case "status":
                {
                    if (!AccountGuard.Allows(snapshot, account, StatusChange)) throw new UnauthorizedAccessException("تغییر وضعیت حساب مجاز نیست.");
                    var reason = command.Get("reason") ?? throw new InvalidOperationException("دلیل الزامی است.");
                    if (account.Status == CustomerStatus.Inactive)
                    {
                        if (data.Find<CustomerMergeOperation>(x => x.MergedCustomerId == account.Id && x.Status == CustomerMergeStatus.Merged).Any())
                            throw new InvalidOperationException("این حساب در حساب دیگری ادغام شده است؛ فعال‌سازی فقط با بازگردانی ادغام ممکن است.");
                        account.Activate();
                        AccountGuard.Log(data, account, CustomerTimelineType.StatusChanged, "حساب فعال شد", reason, userId, nowUtc);
                    }
                    else
                    {
                        account.Deactivate();
                        AccountGuard.Log(data, account, CustomerTimelineType.StatusChanged, "حساب غیرفعال شد", reason, userId, nowUtc);
                    }
                    break;
                }
                case "classify":
                {
                    if (!AccountGuard.Allows(snapshot, account, "Customer.Update")) throw new UnauthorizedAccessException("ویرایش حساب مجاز نیست.");
                    AccountGuard.EnsureMutable(account);
                    var before = $"{RelationshipLabel(account.RelationshipType)} · {string.Join("، ", account.TagList)}";
                    account.Classify(Parse<AccountRelationship>(command.Get("relationship"), "نوع رابطه"),
                        (command.Get("tags") ?? string.Empty).Split([',', '،', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                    AccountGuard.Log(data, account, CustomerTimelineType.StatusChanged, "نوع رابطه / برچسب‌ها تغییر کرد",
                        $"{before} ← {RelationshipLabel(account.RelationshipType)} · {string.Join("، ", account.TagList)}", userId, nowUtc);
                    break;
                }
                default: throw new KeyNotFoundException();
            }
            return true;
        });
    }

    // ───────────────────────────── parsing helpers ─────────────────────────────

    private sealed class FormValues(IReadOnlyDictionary<string, string?>? values)
    {
        public string? this[string key] => values is not null && values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
        public Guid Operation => Guid.TryParse(this["operationId"], out var id) && id != Guid.Empty ? id : Guid.NewGuid();
    }

    private static bool Flag(string? value) => value is "true" or "on" or "1" or "True";

    private static TEnum Parse<TEnum>(string? value, string label) where TEnum : struct, System.Enum =>
        System.Enum.TryParse<TEnum>(value, true, out var parsed) && System.Enum.IsDefined(parsed) ? parsed : throw new InvalidOperationException($"{label} معتبر نیست.");

    private static decimal Money(string? value, string label) =>
        OptionalMoney(value, label) ?? throw new InvalidOperationException($"{label} الزامی است.");

    private static decimal? OptionalMoney(string? value, string label)
    {
        var text = PersianText.Normalize(value)?.Replace(",", "").Replace("٬", "").Replace(" ", "");
        if (string.IsNullOrEmpty(text)) return null;
        return decimal.TryParse(text, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var amount)
            ? amount : throw new InvalidOperationException($"{label} را عددی وارد کنید.");
    }

    private static DateOnly Date(string? value, string label) =>
        OptionalDate(value, label) ?? throw new InvalidOperationException($"{label} الزامی است.");

    private static DateOnly? OptionalDate(string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return JalaliDate.TryParse(value, out var date) ? date : throw new InvalidOperationException($"{label}: تاریخ را به شکل شمسی ۱۴۰۵/۰۷/۱۲ وارد کنید.");
    }

    private static Guid? OptionalGuid(string? value) => Guid.TryParse(value, out var id) && id != Guid.Empty ? id : null;

    private static Guid? OptionalContact(CrmDataSet data, Customer account, string? value)
    {
        if (OptionalGuid(value) is not { } id) return null;
        return data.Find<CustomerContact>(x => x.Id == id && x.CustomerId == account.Id && x.IsActive).Any()
            ? id : throw new InvalidOperationException("فرد رابط انتخاب‌شده به این حساب تعلق ندارد.");
    }
}
