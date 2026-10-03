using Crm.Application.Abstractions;
using Crm.Application.Contracts;
using Crm.Domain.Channel;
using Crm.Domain.Common;
using Crm.Domain.Identity;
using Crm.Domain.Organization;

namespace Crm.Application.Services;

public interface IDealerAssuranceService
{
    DealerAssuranceDto? Get(Guid currentUserId, OrganizationSelection organization, Guid dealerId, DateTimeOffset nowUtc);
    DealerGuaranteeDto AddGuarantee(Guid currentUserId, OrganizationSelection organization, Guid dealerId, SaveDealerGuaranteeCommand command, DateTimeOffset nowUtc);
    DealerGuaranteeDto DecideGuarantee(Guid currentUserId, OrganizationSelection organization, Guid dealerId, Guid guaranteeId,
        DecideDealerGuaranteeCommand command, DateTimeOffset nowUtc);
    DealerTrainingDto AddTraining(Guid currentUserId, OrganizationSelection organization, Guid dealerId, SaveDealerTrainingCommand command, DateTimeOffset nowUtc);
}

/// <summary>
/// Roadmap phase 7: dealer guarantees (security against credit) and training records. Guarantee coverage compares the
/// effective guarantees with the dealer's credit limit from the accounting projection.
/// </summary>
public sealed class DealerAssuranceService(ICrmDataStore store, IAccessSnapshotService access) : IDealerAssuranceService
{
    public const int ExpiryWarningDays = 30;

    public DealerAssuranceDto? Get(Guid currentUserId, OrganizationSelection organization, Guid dealerId, DateTimeOffset nowUtc)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        var readGuarantees = Has(snapshot, organization.CompanyId, "Dealer.Guarantee.Read");
        var readTrainings = Has(snapshot, organization.CompanyId, "Dealer.Training.Read");
        if (!readGuarantees && !readTrainings) throw new UnauthorizedAccessException("Dealer.Guarantee.Read or Dealer.Training.Read is required.");
        var today = DateOnly.FromDateTime(nowUtc.UtcDateTime);
        return store.Read(data =>
        {
            var dealer = data.Find<Dealer>(x => x.Id == dealerId).SingleOrDefault(x => InContext(snapshot, organization, "Dealer.Read", x));
            if (dealer is null) return null;
            var guarantees = readGuarantees && Allows(snapshot, organization, "Dealer.Guarantee.Read", dealer)
                ? data.Find<DealerGuarantee>(x => x.DealerId == dealer.Id).OrderBy(x => x.Status).ThenBy(x => x.ExpiresOn ?? DateOnly.MaxValue).ToList() : [];
            var trainings = readTrainings && Allows(snapshot, organization, "Dealer.Training.Read", dealer)
                ? data.Find<DealerTraining>(x => x.DealerId == dealer.Id).OrderByDescending(x => x.HeldOn).ToList() : [];
            var effective = guarantees.Where(x => x.IsEffective(today)).Sum(x => x.Amount);
            decimal? creditLimit = Has(snapshot, organization.CompanyId, "Dealer.Financial.Read")
                ? data.Find<DealerFinancialSnapshot>(x => x.DealerId == dealer.Id).OrderByDescending(x => x.SynchronizedAtUtc).FirstOrDefault()?.CreditLimit
                : null;
            return new DealerAssuranceDto(dealer.Id, dealer.Code, dealer.TradeName,
                guarantees.Select(x => Map(x, today)).ToList(), trainings.Select(x => Map(x, today)).ToList(), effective, creditLimit,
                creditLimit is > 0 && readGuarantees ? Math.Round(effective / creditLimit.Value * 100m, 1) : null,
                guarantees.Count(x => x.ExpiresWithin(today, ExpiryWarningDays)),
                trainings.Where(x => x.HeldOn > today.AddYears(-1)).Sum(x => x.Hours),
                readGuarantees, Allows(snapshot, organization, "Dealer.Guarantee.Manage", dealer),
                readTrainings, Allows(snapshot, organization, "Dealer.Training.Manage", dealer));
        });
    }

    public DealerGuaranteeDto AddGuarantee(Guid currentUserId, OrganizationSelection organization, Guid dealerId, SaveDealerGuaranteeCommand command,
        DateTimeOffset nowUtc)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        if (!Enum.IsDefined(command.Type)) throw new InvalidOperationException("نوع تضمین معتبر نیست.");
        var issued = Date(command.IssuedOn, "تاریخ صدور") ?? throw new InvalidOperationException("تاریخ صدور الزامی است.");
        var expires = Date(command.ExpiresOn, "تاریخ سررسید");
        return store.Write(data =>
        {
            var dealer = Dealer(data, snapshot, organization, dealerId, "Dealer.Guarantee.Manage");
            var number = PersianText.Normalize(command.Number);
            if (number is not null && data.Find<DealerGuarantee>(x => x.DealerId == dealer.Id && x.Number == number && x.Type == command.Type)
                    .Any(x => x.Status == DealerGuaranteeStatus.Active))
                throw new InvalidOperationException("تضمین فعال دیگری با همین نوع و شماره برای این نماینده ثبت شده است.");
            var guarantee = new DealerGuarantee(Guid.NewGuid(), dealer.Id, dealer.CompanyId, dealer.BranchId, dealer.TerritoryId, command.Type,
                number ?? string.Empty, command.Issuer, command.Amount, issued, expires, command.Notes, currentUserId);
            data.DealerGuarantees.Add(guarantee);
            Audit(data, currentUserId, "Dealer.GuaranteeRegistered", $"{dealer.Code} {command.Type} {guarantee.Number} {guarantee.Amount}", nowUtc);
            return Map(guarantee, DateOnly.FromDateTime(nowUtc.UtcDateTime));
        });
    }

    public DealerGuaranteeDto DecideGuarantee(Guid currentUserId, OrganizationSelection organization, Guid dealerId, Guid guaranteeId,
        DecideDealerGuaranteeCommand command, DateTimeOffset nowUtc)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        return store.Write(data =>
        {
            var dealer = Dealer(data, snapshot, organization, dealerId, "Dealer.Guarantee.Manage");
            var guarantee = data.Find<DealerGuarantee>(x => x.Id == guaranteeId && x.DealerId == dealer.Id).SingleOrDefault() ??
                throw new KeyNotFoundException("تضمین پیدا نشد.");
            if (guarantee.Version != command.ExpectedVersion) throw new InvalidOperationException("تضمین تغییر کرده است؛ صفحه را تازه‌سازی کنید.");
            if (command.Forfeit) guarantee.Forfeit(currentUserId, command.Reason ?? string.Empty, nowUtc);
            else guarantee.Release(currentUserId, command.Reason ?? string.Empty, nowUtc);
            Audit(data, currentUserId, command.Forfeit ? "Dealer.GuaranteeForfeited" : "Dealer.GuaranteeReleased",
                $"{dealer.Code} {guarantee.Number} {guarantee.Amount}: {guarantee.DecisionReason}", nowUtc);
            return Map(guarantee, DateOnly.FromDateTime(nowUtc.UtcDateTime));
        });
    }

    public DealerTrainingDto AddTraining(Guid currentUserId, OrganizationSelection organization, Guid dealerId, SaveDealerTrainingCommand command,
        DateTimeOffset nowUtc)
    {
        var snapshot = RequiredSnapshot(currentUserId);
        if (!Enum.IsDefined(command.Topic)) throw new InvalidOperationException("موضوع آموزش معتبر نیست.");
        var held = Date(command.HeldOn, "تاریخ برگزاری") ?? throw new InvalidOperationException("تاریخ برگزاری الزامی است.");
        var certificate = Date(command.CertificateValidTo, "اعتبار گواهی");
        var today = DateOnly.FromDateTime(nowUtc.UtcDateTime);
        return store.Write(data =>
        {
            var dealer = Dealer(data, snapshot, organization, dealerId, "Dealer.Training.Manage");
            var training = new DealerTraining(Guid.NewGuid(), dealer.Id, dealer.CompanyId, dealer.BranchId, dealer.TerritoryId, command.Title ?? string.Empty,
                command.Topic, held, command.Hours, command.Participants, command.Score, certificate, currentUserId, today);
            data.DealerTrainings.Add(training);
            Audit(data, currentUserId, "Dealer.TrainingRecorded", $"{dealer.Code} {training.Title} {training.Hours}h", nowUtc);
            return Map(training, today);
        });
    }

    private static DateOnly? Date(string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return JalaliDate.TryParse(value, out var date) ? date : throw new InvalidOperationException($"{label} را به شکل شمسی ۱۴۰۵/۰۷/۰۱ وارد کنید.");
    }

    private static Dealer Dealer(CrmDataSet data, AccessSnapshot snapshot, OrganizationSelection organization, Guid dealerId, string permission)
    {
        var dealer = data.Find<Dealer>(x => x.Id == dealerId).SingleOrDefault(x => InContext(snapshot, organization, "Dealer.Read", x)) ??
            throw new KeyNotFoundException("نماینده در دامنهٔ جاری پیدا نشد.");
        if (!Allows(snapshot, organization, permission, dealer)) throw new UnauthorizedAccessException($"{permission} permission is required.");
        return dealer;
    }

    private static DealerGuaranteeDto Map(DealerGuarantee x, DateOnly today) => new(x.Id, x.Type, x.Number, x.Issuer, x.Amount,
        JalaliDate.Format(x.IssuedOn), x.ExpiresOn is { } e ? JalaliDate.Format(e) : null, x.Status, x.IsEffective(today),
        x.ExpiresWithin(today, ExpiryWarningDays), x.Notes, x.DecisionReason, x.Version);

    private static DealerTrainingDto Map(DealerTraining x, DateOnly today) => new(x.Id, x.Title, x.Topic, JalaliDate.Format(x.HeldOn), x.Hours,
        x.Participants, x.Score, x.CertificateValidTo is { } c ? JalaliDate.Format(c) : null, x.CertificateValidTo < today);

    private static void Audit(CrmDataSet data, Guid actor, string eventType, string reason, DateTimeOffset nowUtc) =>
        data.Append(new SecurityAuditEvent(Guid.NewGuid(), nowUtc, eventType, "Success", actor, null, null,
            Guid.NewGuid().ToString("N"), reason.Length <= 1000 ? reason : reason[..1000], string.Empty, string.Empty));

    private AccessSnapshot RequiredSnapshot(Guid userId) => access.Get(userId) ??
        throw new UnauthorizedAccessException("No active access snapshot was found.");

    private static bool Has(AccessSnapshot snapshot, string companyId, string permission) => snapshot.PermissionsFor(companyId).Contains(permission);

    private static bool Allows(AccessSnapshot snapshot, OrganizationSelection organization, string permission, Dealer dealer) =>
        Has(snapshot, organization.CompanyId, permission) && snapshot.AllowsRecord(dealer.CompanyId, permission, dealer.BranchId, dealer.TerritoryId);

    private static bool InContext(AccessSnapshot snapshot, OrganizationSelection organization, string permission, IOrganizationScoped entity) =>
        string.Equals(entity.CompanyId, organization.CompanyId, StringComparison.OrdinalIgnoreCase) &&
        (organization.BranchId is null || string.Equals(entity.BranchId, organization.BranchId, StringComparison.OrdinalIgnoreCase)) &&
        (organization.TerritoryId is null || string.Equals(entity.TerritoryId, organization.TerritoryId, StringComparison.OrdinalIgnoreCase)) &&
        snapshot.AllowsRecord(entity.CompanyId, permission, entity.BranchId, entity.TerritoryId);
}
