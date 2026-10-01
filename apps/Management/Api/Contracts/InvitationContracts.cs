namespace PS.Management.Api.Contracts;

public record InvitationCreatedResponse(Guid InvitationId, string Url, DateTime ExpiresUtc);

public record InvitationSummary(Guid Id, DateTime ExpiresUtc, DateTime? AcceptedUtc, DateTime? RevokedUtc, string Status);

public record InvitationPreviewResponse(string DisplayName, List<string> Applications, DateTime ExpiresUtc);

public record InvitationAcceptedResponse(string DisplayName);
