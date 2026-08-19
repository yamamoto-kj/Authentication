namespace Authentication.Api.Contracts.Admin;

public sealed record DefinirSenhaRequest(string Cpf, string Token, string NovaSenha);
