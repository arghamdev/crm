using Crm.Application.Abstractions;
using Crm.Domain.Accounts;
using Crm.Domain.Channel;

namespace Crm.Application.Services;

/// <summary>
/// Moves the account-file records (activities, notes, documents, payments, contracts, …) on merge and back on unmerge.
/// The ids moved are kept in the merge manifest so an unmerge returns exactly those records. Memberships the survivor
/// already has (same document, campaign or target list) stay on the merged account instead of creating duplicates.
/// </summary>
public static class AccountMerge
{
    public static Dictionary<string, Guid[]> Collect(CrmDataSet data, Guid fromId, Guid toId)
    {
        var survivorDocuments = data.Find<DocumentLink>(x => x.CustomerId == toId).Select(x => x.DocumentId).ToHashSet();
        var survivorCampaigns = data.Find<CampaignMember>(x => x.CustomerId == toId).Select(x => x.CampaignId).ToHashSet();
        var survivorLists = data.Find<TargetListMember>(x => x.CustomerId == toId).Select(x => x.TargetListId).ToHashSet();
        return new Dictionary<string, Guid[]>
        {
            ["activities"] = data.Find<CrmActivity>(x => x.CustomerId == fromId).Select(x => x.Id).ToArray(),
            ["notes"] = data.Find<AccountNote>(x => x.CustomerId == fromId).Select(x => x.Id).ToArray(),
            ["documentLinks"] = data.Find<DocumentLink>(x => x.CustomerId == fromId).Where(x => !survivorDocuments.Contains(x.DocumentId)).Select(x => x.Id).ToArray(),
            ["payments"] = data.Find<AccountPayment>(x => x.CustomerId == fromId).Select(x => x.Id).ToArray(),
            ["bankAccounts"] = data.Find<AccountBankAccount>(x => x.CustomerId == fromId).Select(x => x.Id).ToArray(),
            ["contracts"] = data.Find<AccountContract>(x => x.CustomerId == fromId).Select(x => x.Id).ToArray(),
            ["projects"] = data.Find<AccountProject>(x => x.CustomerId == fromId).Select(x => x.Id).ToArray(),
            ["participations"] = data.Find<AccountParticipation>(x => x.CustomerId == fromId).Select(x => x.Id).ToArray(),
            ["allocations"] = data.Find<AccountAllocation>(x => x.CustomerId == fromId).Select(x => x.Id).ToArray(),
            ["surveyResponses"] = data.Find<SurveyResponse>(x => x.CustomerId == fromId).Select(x => x.Id).ToArray(),
            ["campaignMembers"] = data.Find<CampaignMember>(x => x.CustomerId == fromId).Where(x => !survivorCampaigns.Contains(x.CampaignId)).Select(x => x.Id).ToArray(),
            ["targetListMembers"] = data.Find<TargetListMember>(x => x.CustomerId == fromId).Where(x => !survivorLists.Contains(x.TargetListId)).Select(x => x.Id).ToArray(),
            ["guarantees"] = data.Find<DealerGuarantee>(x => x.CustomerId == fromId).Select(x => x.Id).ToArray()
        };
    }

    public static void Move(CrmDataSet data, IReadOnlyDictionary<string, Guid[]>? manifest, Guid toId)
    {
        if (manifest is null) return;
        Guid[] Ids(string key) => manifest.TryGetValue(key, out var ids) ? ids : [];
        var ids = Ids("activities"); if (ids.Length > 0) foreach (var x in data.Find<CrmActivity>(x => ids.Contains(x.Id))) x.ReassignCustomer(toId);
        var notes = Ids("notes"); if (notes.Length > 0) foreach (var x in data.Find<AccountNote>(x => notes.Contains(x.Id))) x.ReassignCustomer(toId);
        var links = Ids("documentLinks"); if (links.Length > 0) foreach (var x in data.Find<DocumentLink>(x => links.Contains(x.Id))) x.ReassignCustomer(toId);
        var payments = Ids("payments"); if (payments.Length > 0) foreach (var x in data.Find<AccountPayment>(x => payments.Contains(x.Id))) x.ReassignCustomer(toId);
        var banks = Ids("bankAccounts"); if (banks.Length > 0) foreach (var x in data.Find<AccountBankAccount>(x => banks.Contains(x.Id))) x.ReassignCustomer(toId);
        var contracts = Ids("contracts"); if (contracts.Length > 0) foreach (var x in data.Find<AccountContract>(x => contracts.Contains(x.Id))) x.ReassignCustomer(toId);
        var projects = Ids("projects"); if (projects.Length > 0) foreach (var x in data.Find<AccountProject>(x => projects.Contains(x.Id))) x.ReassignCustomer(toId);
        var participations = Ids("participations"); if (participations.Length > 0) foreach (var x in data.Find<AccountParticipation>(x => participations.Contains(x.Id))) x.ReassignCustomer(toId);
        var allocations = Ids("allocations"); if (allocations.Length > 0) foreach (var x in data.Find<AccountAllocation>(x => allocations.Contains(x.Id))) x.ReassignCustomer(toId);
        var responses = Ids("surveyResponses"); if (responses.Length > 0) foreach (var x in data.Find<SurveyResponse>(x => responses.Contains(x.Id))) x.ReassignCustomer(toId);
        var members = Ids("campaignMembers"); if (members.Length > 0) foreach (var x in data.Find<CampaignMember>(x => members.Contains(x.Id))) x.ReassignCustomer(toId);
        var listMembers = Ids("targetListMembers"); if (listMembers.Length > 0) foreach (var x in data.Find<TargetListMember>(x => listMembers.Contains(x.Id))) x.ReassignCustomer(toId);
        var guarantees = Ids("guarantees"); if (guarantees.Length > 0) foreach (var x in data.Find<DealerGuarantee>(x => guarantees.Contains(x.Id))) x.ReassignCustomer(toId);
    }
}
