using Crm.Application.Contracts;
using Crm.Application.Services;
using Crm.Domain.Common;
using Crm.Domain.Customers;
using Crm.Infrastructure.Data;
using Crm.Infrastructure.Identity;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;

internal static class CustomerProfileChecks
{
    internal static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52];

    internal static void Run(Action<bool, string> check)
    {
        CheckIdentifiersAndDates(check);
        CheckFormRules(check);
        CheckCreateEditAndContacts(check);
    }

    /// <summary>Builds a valid individual national code from its first nine digits.</summary>
    internal static string NationalCode(string nine)
    {
        var sum = 0;
        for (var i = 0; i < 9; i++) sum += (nine[i] - '0') * (10 - i);
        var r = sum % 11;
        return nine + (r < 2 ? r : 11 - r);
    }

    /// <summary>Builds a valid legal national ID from its first ten digits.</summary>
    internal static string LegalId(string ten)
    {
        int[] coefficients = [29, 27, 23, 19, 17, 29, 27, 23, 19, 17];
        var offset = ten[9] - '0' + 2;
        var sum = 0;
        for (var i = 0; i < 10; i++) sum += (ten[i] - '0' + offset) * coefficients[i];
        var r = sum % 11;
        return ten + (r == 10 ? 0 : r);
    }

    private static void CheckIdentifiersAndDates(Action<bool, string> check)
    {
        var code = NationalCode("001234567");
        var wrong = code[..9] + (char)('0' + (code[9] - '0' + 1) % 10);
        check(IranianIdentifiers.IsValidNationalCode(code) && !IranianIdentifiers.IsValidNationalCode(wrong) &&
              !IranianIdentifiers.IsValidNationalCode("1111111111") && !IranianIdentifiers.IsValidNationalCode("12345"),
            "CUSTF: national code check digit, length and repeated-digit rules.");
        var legal = LegalId("1010123456");
        check(IranianIdentifiers.IsValidLegalNationalId(legal) && !IranianIdentifiers.IsValidLegalNationalId(legal[..10] + (char)('0' + (legal[10] - '0' + 1) % 10)),
            "CUSTF: legal national ID check digit.");
        check(PersianText.Digits("۰۹۱۲-۳۴۵ ۶۷۸۹") == "09123456789" && PersianText.Normalize("علي كاظمي") == "علی کاظمی" &&
              IranianIdentifiers.NormalizeMobile("+98 912 345 6789") == "09123456789" && IranianIdentifiers.NormalizeMobile("9123456789") == "09123456789",
            "CUSTF: Persian/Arabic digits and letters are normalized and +98 mobiles become 09xxxxxxxxx.");
        check(IranianIdentifiers.IsValidPostalCode("1599911111") && !IranianIdentifiers.IsValidPostalCode("0123456789") &&
              IranianIdentifiers.IsValidLandline("02188776655") && IranianIdentifiers.IsValidLandline("88776655") && !IranianIdentifiers.IsValidLandline("09121234567"),
            "CUSTF: postal code and landline formats.");
        check(JalaliDate.TryParse("۱۴۰۳/۰۱/۰۱", out var nowruz) && nowruz == new DateOnly(2024, 3, 20) && JalaliDate.Format(nowruz) == "1403/01/01" &&
              !JalaliDate.TryParse("1403/12/31", out _) && JalaliDate.TryParse("1403/12/30", out _) && !JalaliDate.TryParse("2024-03-20", out _),
            "CUSTF: Jalali dates parse (incl. Persian digits and leap Esfand) and format back.");
        check(JalaliDate.MonthStart(1405, 7) == new DateTimeOffset(2026, 9, 23, 0, 0, 0, TimeSpan.Zero) &&
              JalaliDate.NextMonthStart(1405, 12) == JalaliDate.MonthStart(1406, 1) && JalaliDate.MonthLabel(1405, 7) == "مهر 1405",
            "CUSTF: Jalali month boundaries.");
    }

    private static void CheckFormRules(Action<bool, string> check)
    {
        var individual = Errors(() => CustomerFormRules.Normalize(CustomerKind.Individual,
            new CustomerProfileInput(NationalCode: "1234567890", Mobile1: "0912", PostalCode: "123", BirthDate: "1370/13/01", Website: "not a site"),
            [new ContactPersonInput(Position: "خرید", Mobile: "021")], "bad@"));
        check(individual.Keys.ToHashSet().SetEquals(["Profile.FirstName", "Profile.LastName", "Profile.NationalCode", "Profile.Mobile1",
                "Profile.PostalCode", "Profile.BirthDate", "Profile.Website", "PrimaryEmail", "Contacts[0].LastName", "Contacts[0].Mobile"]),
            "CUSTF: every invalid individual field is reported against its own input: " + string.Join(",", individual.Keys));

        var person = CustomerFormRules.Normalize(CustomerKind.Individual,
            new CustomerProfileInput(FirstName: "سارا", LastName: "کریمی", NationalCode: NationalCode("004512345"), Mobile1: "۰۹۱۲۱۲۳۴۵۶۷",
                RegistrationNumber: "999", LegalNationalId: "123", BirthDate: "1365/02/10"), null, "Sara@Mail.TEST");
        check(person.NationalId == NationalCode("004512345") && person.PrimaryPhone == "09121234567" && person.Profile.RegistrationNumber is null &&
              person.Profile.BirthDate == new DateOnly(1986, 4, 30) && person.PrimaryEmail == "Sara@Mail.TEST",
            "CUSTF: an individual's national code becomes the identifier; company-only fields left from switching type are dropped.");

        var company = CustomerFormRules.Normalize(CustomerKind.Legal,
            new CustomerProfileInput(LegalNationalId: LegalId("1410234567"), NationalCode: NationalCode("001112223"), Phone1: "021-8877-6655",
                EconomicCode: "411111111111", Website: "sepehr.test"),
            [new ContactPersonInput(), new ContactPersonInput(LastName: "احمدی", Mobile: "09120000001"), new ContactPersonInput(FirstName: "رضا", Phone: "02122334455")],
            null);
        check(company.NationalId == LegalId("1410234567") && company.Profile.RepresentativeNationalCode == NationalCode("001112223") &&
              company.PrimaryPhone == "02188776655" && company.Profile.Website == "https://sepehr.test" && company.Contacts.Count == 2 &&
              company.Contacts[0].IsPrimary && !company.Contacts[1].IsPrimary,
            "CUSTF: a company keeps its legal ID and the representative's national code; empty contact rows are skipped and the first becomes primary.");
        var badLegal = Errors(() => CustomerFormRules.Normalize(CustomerKind.Legal, new CustomerProfileInput(LegalNationalId: "10101234560",
            ActivityType: "نامعتبر"), [new ContactPersonInput(LastName: "الف", IsPrimary: true), new ContactPersonInput(LastName: "ب", IsPrimary: true)], null));
        check(badLegal.ContainsKey("Profile.LegalNationalId") && badLegal.ContainsKey("Profile.ActivityType") && badLegal.ContainsKey("Contacts"),
            "CUSTF: invalid legal ID, an option outside the list and two primary contacts are rejected.");
        check(CustomerFormRules.Normalize(CustomerKind.Legal, new CustomerProfileInput(LegalNationalId: "10101234567"), null, null, "10101234567").NationalId == "10101234567",
            "CUSTF: an unchanged legacy identifier is not re-validated on edit.");
    }

    private static void CheckCreateEditAndContacts(Action<bool, string> check)
    {
        using var store = new InMemoryCrmDataStore();
        using var provider = new ServiceCollection().AddDistributedMemoryCache().BuildServiceProvider();
        var access = new DemoAccessSnapshotService(store, provider.GetRequiredService<IDistributedCache>());
        var crm = new CrmApplicationService(store, access, new InMemoryCustomerQueryStore(store));
        var customer360 = new Customer360Service(store, access);
        Guid User(int n) => Guid.Parse($"10000000-0000-4000-8000-{n:000000000000}");
        var manager = User(1);
        var executive = User(9);
        var org = new OrganizationSelection("C01", null, null);
        var now = DateTimeOffset.UtcNow;

        var legalId = LegalId("1420987654");
        var created = crm.CreateCustomer(manager, org, new CreateCustomerCommand("کجور", "شیراز", "سارا احمدی", "B01", "استاندارد",
            CustomerKind.Legal, PrimaryEmail: "info@kojoor.test",
            Profile: new CustomerProfileInput("بازرگانی و پخش", "آقای", "حمید", "کجوری", "مدیرعامل", "09171234567", null, "07132223344", null,
                "فارس", "خیابان زند، پلاک ۸", "7134567891", null, "شرکت بازرگانی کجور", "411222333444", legalId, null, "8899",
                AcquaintanceDate: "1402/07/01", ReferralSource: "نمایشگاه"),
            Contacts: [new ContactPersonInput("خانم", "لیلا", "کاظمی", "مدیر خرید", "09170000001", "07132223345", "12", "buy@kojoor.test")]),
            new CustomerLogoUpload(Png));
        var view = customer360.Get(manager, org, created.Id, includeRelatedActivity: false)!;
        check(created.NationalId == legalId && created.PrimaryPhone == "07132223344" && view.Profile is { HasLogo: true, LegalName: "شرکت بازرگانی کجور",
                RegistrationNumber: "8899", AcquaintanceDate: "1402/07/01", Masked: false } &&
              view.Addresses.Single() is { Type: CustomerAddressType.Registered, Province: "فارس", City: "شیراز", PostalCode: "7134567891", IsPrimary: true } &&
              view.Contacts.Single() is { FullName: "خانم لیلا کاظمی", Mobile: "09170000001", Extension: "12", IsPrimary: true, Role: "مدیر خرید" },
            "CUSTF: a legal customer is created with profile, registered address, logo and its contact person.");
        check(customer360.GetLogo(manager, org, created.Id) is { ContentType: "image/png" } && customer360.GetLogo(User(3), org, created.Id) is null,
            "CUSTF: the logo is served by signature content type and only inside the reader's scope.");

        var masked = customer360.Get(executive, org, created.Id, includeRelatedActivity: false)!;
        check(masked.Profile is { Masked: true } profile && profile.Mobile1 != "09171234567" && masked.Contacts.Single().Mobile != "09170000001",
            "CUSTF: mobiles of the profile and contacts are masked without the contact permission.");

        var individualCode = NationalCode("005566778");
        var person = crm.CreateCustomer(manager, org, new CreateCustomerCommand("فروشگاه ناصری", "تهران", "سارا احمدی", "B01", "استاندارد",
            CustomerKind.Individual, Profile: new CustomerProfileInput(FirstName: "مهدی", LastName: "ناصری", Mobile1: "09351112233", NationalCode: individualCode)));
        var personContacts = customer360.Get(manager, org, person.Id, includeRelatedActivity: false)!.Contacts;
        check(person.NationalId == individualCode && person.Kind == CustomerKind.Individual &&
              personContacts.Single() is { FullName: "مهدی ناصری", Mobile: "09351112233", IsPrimary: true },
            "CUSTF: an individual's national code is the identifier and the person becomes the primary contact.");
        Reject<InvalidOperationException>(() => crm.CreateCustomer(manager, org, new CreateCustomerCommand("تکراری", "تهران", "سارا احمدی", "B01", "استاندارد",
            CustomerKind.Individual, Profile: new CustomerProfileInput(FirstName: "الف", LastName: "ب", NationalCode: individualCode))), "a duplicate national code", check);
        Reject<CustomerValidationException>(() => crm.CreateCustomer(manager, org, new CreateCustomerCommand("بی‌نام", "تهران", "سارا احمدی", "B01", "استاندارد",
            CustomerKind.Individual, Profile: new CustomerProfileInput())), "an individual without names", check);
        Reject<InvalidOperationException>(() => crm.CreateCustomer(manager, org, new CreateCustomerCommand("لوگو بد", "تهران", "سارا احمدی", "B01", "استاندارد",
            Profile: new CustomerProfileInput()), new CustomerLogoUpload([1, 2, 3, 4])), "a logo that is not an image", check);

        var edit = customer360.GetEdit(manager, org, created.Id)!;
        customer360.Update(manager, org, created.Id, new UpdateCustomerCommand(edit.Name, edit.City, edit.Owner, edit.BranchId, edit.TerritoryId,
            edit.Segment, null, null, "info@kojoor.test", "", edit.ExpectedVersion, edit.Profile! with { Mobile2 = "09179998877", Address = "خیابان زند، پلاک ۱۰" }),
            now, removeLogo: true);
        var edited = customer360.Get(manager, org, created.Id, includeRelatedActivity: false)!;
        check(edited.Profile is { Mobile2: "09179998877", HasLogo: false } && edited.Addresses.Single().AddressLine == "خیابان زند، پلاک ۱۰" &&
              edited.Customer.NationalId == legalId && customer360.GetLogo(manager, org, created.Id) is null,
            "CUSTF: editing updates the profile and registered address in place and can remove the logo.");
        var sepehr = Guid.Parse("20000000-0000-4000-8000-000000000001");
        var legacy = customer360.GetEdit(manager, org, sepehr)!;
        customer360.Update(manager, org, sepehr, new UpdateCustomerCommand(legacy.Name, legacy.City, legacy.Owner, legacy.BranchId, legacy.TerritoryId,
            legacy.Segment, null, null, legacy.PrimaryEmail, "", legacy.ExpectedVersion, legacy.Profile), now);
        check(customer360.Get(manager, org, sepehr, false)!.Customer.NationalId == "10101234567",
            "CUSTF: a seeded customer with a legacy identifier can still be edited.");

        customer360.SaveContactPerson(manager, org, created.Id, null,
            new SaveContactPersonCommand(new ContactPersonInput("آقای", "کامران", "نیکو", "حسابدار", "09171112233", null, null, null, null, true), ContactConsentStatus.Granted, 0), now);
        var contacts = customer360.Get(manager, org, created.Id, false)!.Contacts.Where(x => x.IsActive).ToList();
        var kamran = contacts.Single(x => x.LastName == "نیکو");
        check(kamran.IsPrimary && contacts.Count(x => x.IsPrimary) == 1, "CUSTF: a new primary contact takes over the primary flag.");
        customer360.SaveContactPerson(manager, org, created.Id, kamran.Id,
            new SaveContactPersonCommand(new ContactPersonInput("آقای", "کامران", "نیکوکار", "حسابدار ارشد", "09171112233", "07130000000", "5", null, null, true),
                ContactConsentStatus.Granted, kamran.Version), now);
        Reject<InvalidOperationException>(() => customer360.SaveContactPerson(manager, org, created.Id, kamran.Id,
            new SaveContactPersonCommand(new ContactPersonInput(LastName: "x", Mobile: "09170000000"), ContactConsentStatus.Unknown, kamran.Version), now), "a stale contact version", check);
        var renamed = customer360.Get(manager, org, created.Id, false)!.Contacts.Single(x => x.Id == kamran.Id);
        customer360.DeactivateContact(manager, org, created.Id, kamran.Id, renamed.Version, now);
        var remaining = customer360.Get(manager, org, created.Id, false)!.Contacts.Where(x => x.IsActive).ToList();
        check(renamed is { FullName: "آقای کامران نیکوکار", Extension: "5", Role: "حسابدار ارشد" } && remaining.Single().IsPrimary,
            "CUSTF: a contact is edited with version control and deactivating the primary promotes the next contact.");
        Reject<UnauthorizedAccessException>(() => customer360.SaveContactPerson(executive, org, created.Id, null,
            new SaveContactPersonCommand(new ContactPersonInput(LastName: "x", Mobile: "09170000000"), ContactConsentStatus.Unknown, 0), now), "contact edit without contact permission", check);
    }

    private static IReadOnlyDictionary<string, string> Errors(Action action)
    {
        try { action(); return new Dictionary<string, string>(); }
        catch (CustomerValidationException exception) { return exception.Errors; }
    }

    private static void Reject<T>(Action action, string name, Action<bool, string> check) where T : Exception
    {
        try { action(); check(false, "CUSTF: " + name + " must be rejected."); }
        catch (T) { }
    }
}
