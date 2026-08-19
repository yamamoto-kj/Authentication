namespace Authentication.Api.Contracts.TwoFactor;

public sealed record TotpEnrollStartResponse(string Secret, string OtpauthUri);

public sealed record TotpEnrollConfirmRequest(string Code);

public sealed record TotpVerifyRequest(string Code);

public sealed record WebAuthnEnrollVerifyRequest(string AttestationResponseJson, string? DeviceName);
