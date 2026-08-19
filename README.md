# Authentication — Multi-Tenant REST API (.NET 8)

REST API multi-tenant em .NET 8, com autenticação/autorização OAuth2
(OpenIddict), isolamento de dados por tenant no PostgreSQL, e uma pilha de
resiliência (Polly, rate limiting, idempotência) pensada para suportar
múltiplas instâncias atendendo requisições concorrentes.

## Arquitetura

Clean Architecture em 4 camadas:

```
src/
  Authentication.Domain          entidades e regras de negócio, sem dependências externas
  Authentication.Application     casos de uso (CQRS via MediatR), validação (FluentValidation)
  Authentication.Infrastructure  EF Core, Identity, OpenIddict, Polly, multi-tenancy
  Authentication.Api             controllers, middlewares, pipeline HTTP
tests/
  Authentication.Tests           testes unitários e de infraestrutura
```

A regra de dependência é sempre "de fora para dentro": `Api` depende de
`Infrastructure`/`Application`/`Domain`; `Domain` não depende de nada.

## Identidade e multi-tenancy (CPF Global)

Hierarquia de três níveis: um `Usuario` é uma identidade global (login por
CPF), que pode ter um `Vinculo` (membership) com qualquer número de
`Tenant`s (Grupos Econômicos), cada um com uma ou mais `Empresa`s (CNPJs).

```
Usuario (CPF, global) 1─N Vinculo N─1 Tenant (Grupo Econômico) 1─N Empresa (CNPJ)
                              │
                              └─1 TenantRole ──N─┬N Permission (catálogo global)
```

- **Sem auto-cadastro**: nenhum endpoint cria um `Usuario` livremente — só
  um *platform admin* via `POST /admin/usuarios` (ver "Provisionamento"
  abaixo). Um admin de um tenant pode convidar um CPF **já existente** para
  o seu próprio grupo via `POST /admin/vinculos`, sem precisar de
  privilégio de plataforma.
- **Estratégia de isolamento**: row-level em banco compartilhado — toda
  entidade de negócio implementa `ITenantOwned`/`IEmpresaOwned` e carrega
  `TenantId`/`EmpresaId`.
- **Resolução**: exclusivamente pelas claims `tenant_id`/`empresa_id` do
  JWT validado — nunca por header/rota informado pelo cliente em endpoints
  autenticados.
- **Aplicação do isolamento**: *global query filters* do EF Core
  (`ApplicationDbContext.OnModelCreating`) filtram `Products` por
  `TenantId == CurrentTenantId && EmpresaId == CurrentEmpresaId`,
  reavaliados a cada instância de `DbContext` (ver comentário no código
  sobre a pegadinha clássica de capturar um serviço *scoped* dentro do
  modelo, que fica em cache globalmente).
- **Defesa em profundidade**: `AuditableEntitySaveChangesInterceptor`
  rejeita, no `SaveChanges`, qualquer gravação com `TenantId`/`EmpresaId`
  diferente do contexto autenticado.
- **Permissões**: cada `TenantRole` concede um subconjunto do catálogo
  global de `Permission`s (`PermissionKeys`); o token final carrega essas
  chaves na claim `permissions`, e os endpoints exigem policies
  específicas (ex: `produtos:gerenciar`, `usuarios:convidar`) — nunca
  checam o nome do papel por string.
- **Testes automatizados**: `tests/.../TenantIsolationTests.cs` prova
  isolamento por tenant *e* por empresa (dois usuários do mesmo grupo,
  empresas diferentes, não enxergam os dados um do outro), e que a
  gravação cross-tenant/cross-empresa lança `TenantMismatchException`/
  `EmpresaMismatchException`.

## OAuth2 (OpenIddict) e o fluxo de login

Servidor de autorização próprio (RFC 6749), com um único endpoint de token
físico (`/connect/token`) registrado sob múltiplas URIs — cada uma cobre
uma etapa do fluxo de login, usando *extension grant types* customizados
(RFC 6749 §4.5) para reaproveitar 100% da infraestrutura OAuth (emissão,
revogação, refresh rotativo) em vez de emitir tokens "na mão".

```
CPF+senha (POST /connect/token, grant password)
      │
      ▼
2FA obrigatório para o usuário?
      │ não                              │ sim
      ▼                                  ▼
token de seleção                  já tem fator cadastrado?
(sem tenant/empresa)                 │ não          │ sim
      │                              ▼              ▼
      │                       token de           token de
      │                       enrollment          challenge
      │                       (/auth/2fa/*/enroll/*)  │
      │                                            (/auth/2fa/totp/verify ou
      │                                             /auth/2fa/webauthn/assertion/verify)
      │                                                 │
      └─────────────────────────┬───────────────────────┘
                                 ▼
                  GET /auth/contexts (lista grupos/empresas)
                                 │
                  POST /auth/select-context (tenant_id [+ empresa_id])
                                 ▼
                    token final (tenant_id + empresa_id + role + permissions)
```

| Grant type (URI) | Uso |
|---|---|
| `client_credentials` (`/connect/token`) | Comunicação serviço-a-serviço; o tenant vem de uma *property* do client OAuth. |
| `password` (`/connect/token`) | CPF+senha. Nunca emite token final direto — sempre um token de seleção ou de 2FA. |
| `refresh_token` (`/connect/token`) | *Rolling refresh*: cada uso emite um novo refresh token e revoga o anterior. |
| `urn:authentication:grant-type:tenant_selection` (`/auth/select-context`) | Troca o token de seleção + `tenant_id`/`empresa_id` escolhidos pelo token final. |
| `urn:authentication:grant-type:2fa_totp_verify` (`/auth/2fa/totp/verify`) | Completa o login com um código TOTP. |
| `urn:authentication:grant-type:2fa_webauthn_verify` (`/auth/2fa/webauthn/assertion/verify`) | Completa o login com uma assertion WebAuthn/passkey. |

Access tokens são JWT (RSA + criptografados) e expiram em 15 minutos;
refresh tokens expiram em 14 dias. Em `Development`, certificados de
assinatura/criptografia são efêmeros (`AddDevelopmentSigningCertificate`);
em produção, configure `OpenIddict:SigningCertificate`/
`EncryptionCertificate` com certificados reais (variáveis de ambiente ou
um secret manager — nunca em `appsettings.json` commitado).

Aplicações OAuth e tokens ficam persistidos no Postgres (não em memória),
então qualquer instância da API atrás do load balancer processa qualquer
requisição — sem *sticky sessions*.

## Provisionamento de usuários

Não existe auto-cadastro. Dois caminhos, dois níveis de privilégio:

- **`POST /admin/usuarios`** (policy `PlatformAdmin`, role global de
  plataforma) — cria um `Usuario` novo (CPF/nome/email), sem senha, e
  opcionalmente já com um `Vinculo` inicial. Retorna um token de
  redefinição de senha; em produção isso deve ser enviado por e-mail, não
  devolvido na resposta (não há infraestrutura de e-mail neste projeto
  ainda — a resposta direta é só uma conveniência de desenvolvimento).
- **`POST /admin/vinculos`** (policy `usuarios:convidar`, dentro do
  próprio tenant do chamador) — vincula um CPF **já cadastrado** ao grupo
  econômico de quem chama, sem precisar de privilégio de plataforma.
- **`POST /auth/set-password`** (público, com o token de redefinição) —
  ativa a conta e confirma o e-mail.

## Autenticação de dois fatores (TOTP + WebAuthn)

Parametrizável por usuário e controlada só por um platform admin
(`PATCH /admin/usuarios/{id}/2fa`) — o usuário não pode ligar/desligar a
*exigência* sozinho, mas é sempre ele quem cadastra o próprio fator
(ninguém mais pode gerar um segredo TOTP ou uma chave privada WebAuthn em
nome de outra pessoa).

- **TOTP**: reaproveita o suporte nativo do ASP.NET Identity
  (`AuthenticatorTokenProvider`) — `POST /auth/2fa/totp/enroll/start`
  gera o segredo/QR code, `.../enroll/confirm` valida o primeiro código.
- **WebAuthn/Passkey**: via [Fido2NetLib](https://github.com/passwordless-lib/fido2-net-lib)
  (`Fido2` NuGet). `POST /auth/2fa/webauthn/enroll/options` +
  `.../enroll/verify` fazem a cerimônia de registro; o desafio gerado pelo
  servidor fica em cache (Redis) entre as duas chamadas — nunca confia num
  desafio ecoado pelo cliente.
- O gate de 2FA no login é calculado explicitamente em código (não usa o
  `TwoFactorEnabled`/`RequiresTwoFactor` nativos do Identity), porque a
  detecção automática do Identity só reconhece provedores de token
  (TOTP/SMS/e-mail) — um usuário só com WebAuthn nunca dispararia o gate
  nativo. Configure `WebAuthn:ServerDomain`/`Origins` no `appsettings` para
  bater com o domínio real da aplicação cliente (o navegador rejeita a
  cerimônia se não bater).

## Segurança

- Senhas: bcrypt via ASP.NET Core Identity, política mínima de 12
  caracteres com maiúscula/minúscula/dígito/símbolo, lockout após 5
  tentativas.
- Cada requisição passa por: rate limiting → autenticação → autorização →
  handler. Falha em qualquer etapa nunca revela detalhes internos —
  `GlobalExceptionHandler` mapeia exceções para RFC 7807 (`ProblemDetails`)
  e loga o stack trace só no servidor, correlacionado por `traceId`.
- Cabeçalhos de segurança (`X-Content-Type-Options`, `X-Frame-Options`,
  `Content-Security-Policy`, etc.) em toda resposta; HSTS fora de
  `Development`.
- Validação de entrada obrigatória via FluentValidation antes de qualquer
  handler rodar (`ValidationBehaviour` no pipeline do MediatR).
- CORS restrito a `Cors:AllowedOrigins` (vazio por padrão — nada é
  permitido até ser configurado explicitamente).

## Resiliência e tolerância a múltiplas requisições

- **Rate limiting** (`System.Threading.RateLimiting`, nativo do .NET):
  particionado por tenant nos endpoints de negócio (para um tenant não
  afetar os demais) e por IP no endpoint de token (mitiga
  *credential stuffing*).
- **Concorrência otimista**: toda entidade tem `RowVersion`; um `UPDATE`
  concorrente detectado pelo EF Core vira `409 Conflict` em vez de
  sobrescrever silenciosamente ("*lost update*").
- **Idempotência**: header `Idempotency-Key` em `POST`/`PATCH` — a primeira
  resposta é cacheada (Redis) e replicada para retries com a mesma chave,
  protegendo contra duplicação em reenvios de rede.
- **Polly** nos `HttpClient`s de saída: timeout por tentativa, retry
  exponencial com jitter, circuit breaker — uma dependência externa fora do
  ar não derruba esta API.
- **Retry de conexão com o banco**: `EnableRetryOnFailure` no Npgsql para
  falhas transitórias de rede/DB.
- **Estado compartilhado, não local**: OAuth, rate limiting e idempotência
  vivem em Postgres/Redis, não em memória do processo — a API escala
  horizontalmente sem *sticky sessions* (ver `docker-compose.yml`).

## Rodando localmente

Pré-requisitos: .NET 8 SDK, Docker (ou Postgres 16 + Redis locais).

```bash
# Sobe Postgres + Redis + API
cp .env.example .env   # defina POSTGRES_PASSWORD e REDIS_PASSWORD
docker compose up --build

# Ou, sem Docker, com Postgres/Redis já rodando localmente:
dotnet run --project src/Authentication.Api
```

Em `Development`, o `DbSeeder` roda automaticamente e cria:

- Tenant `demo` (slug `demo`), com duas empresas (Matriz e Filial)
- Client OAuth `demo-service-client` / `demo-service-secret-change-me`
  (`client_credentials`)
- Client OAuth `demo-first-party-client` (público, `password` grant +
  grants customizados de seleção/2FA)
- Usuário demo `52998224725` / `ChangeMe!2026#Secure` — papel `admin`
  (permissões `produtos:gerenciar` e `usuarios:convidar`) no tenant `demo`,
  com acesso às duas empresas
- Platform admin `11144477735` / `ChangeMe!2026#PlatformAdmin` — sem
  nenhum `Vinculo`, só para `POST /admin/usuarios`

### Login completo (CPF → seleção de contexto → token final)

```bash
# 1. CPF + senha -> token de seleção (a menos que só haja 1 grupo/1 empresa
#    e o front decida pular a pergunta, todo login passa por aqui)
SEL=$(curl -s -X POST http://localhost:5080/connect/token \
  -d grant_type=password -d client_id=demo-first-party-client \
  -d username=52998224725 -d password="ChangeMe!2026#Secure" -d scope=api)
SEL_TOKEN=$(echo "$SEL" | jq -r .access_token)

# 2. Lista grupos/empresas disponíveis
curl -s http://localhost:5080/auth/contexts -H "Authorization: Bearer $SEL_TOKEN"

# 3. Escolhe o contexto -> token final
curl -X POST http://localhost:5080/auth/select-context \
  -H "Authorization: Bearer $SEL_TOKEN" \
  -d grant_type=urn:authentication:grant-type:tenant_selection \
  -d client_id=demo-first-party-client \
  -d tenant_id=<tenantId> -d empresa_id=<empresaId>
```

### Chamando a API

```bash
curl http://localhost:5080/api/v1/products \
  -H "Authorization: Bearer <access_token_final>"
```

Swagger UI disponível em `/swagger` (apenas em `Development`).

## Testes

```bash
dotnet test
```

Inclui validação de comandos (FluentValidation) e isolamento de tenant
(EF Core InMemory), incluindo o cenário de regressão do bug de *query
filter* cacheado descrito acima.

## Migrations

```bash
dotnet tool install -g dotnet-ef
dotnet ef migrations add <Nome> \
  --project src/Authentication.Infrastructure \
  --startup-project src/Authentication.Api \
  --output-dir Persistence/Migrations

dotnet ef database update \
  --project src/Authentication.Infrastructure \
  --startup-project src/Authentication.Api
```

## Checklist de produção

- [ ] Substituir os certificados de assinatura/criptografia de
      desenvolvimento por certificados reais gerenciados por um secret
      manager (`OpenIddict:SigningCertificate`/`EncryptionCertificate`).
- [ ] Definir `ConnectionStrings:Default` e `ConnectionStrings:Redis` via
      variáveis de ambiente/secret manager — nunca em `appsettings.json`.
- [ ] Configurar `Cors:AllowedOrigins` com as origens reais.
- [ ] Trocar a senha do usuário/client de demonstração ou remover o
      `DbSeeder` do ambiente de produção (ele só roda em `Development`).
- [ ] Colocar a API atrás de um proxy/load balancer que termina TLS.
- [ ] Configurar `WebAuthn:ServerDomain`/`ServerName`/`Origins` com o
      domínio real da(s) aplicação(ões) cliente.
- [ ] Implementar entrega do token de "definir senha" (`/admin/usuarios`)
      por e-mail — hoje ele volta na resposta HTTP como conveniência de
      desenvolvimento, o que nunca deve acontecer em produção.
