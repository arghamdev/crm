using Crm.Application.Contracts;

namespace Crm.Web.Presentation;

/// <summary>One repeating contact-person row of the customer form: binding key, list position (for error lookup) and values.</summary>
public sealed record ContactRowModel(string Key, int Position, ContactPersonInput Contact);
