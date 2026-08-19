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

## Multi-tenancy

- **Estratégia**: isolamento por linha (row-level) em um banco compartilhado
  — todo recurso de negócio implementa `ITenantOwned` e carrega `TenantId`.
- **Resolução do tenant**: exclusivamente pela claim `tenant_id` do JWT
  validado (nunca por header/rota informado pelo cliente em endpoints
  autenticados — isso evitaria que um chamador se passasse por outro
  tenant apenas trocando um header).
- **Aplicação do isolamento**: um *global query filter* do EF Core
  (`ApplicationDbContext.OnModelCreating`) filtra toda consulta a
  `Products` por `TenantId == CurrentTenantId`, reavaliado a cada instância
  de `DbContext` (ver comentário no código sobre a pegadinha clássica de
  capturar um serviço *scoped* dentro do modelo, que fica em cache
  globalmente).
- **Defesa em profundidade**: um `SaveChangesInterceptor`
  (`AuditableEntitySaveChangesInterceptor`) rejeita, no `SaveChanges`,
  qualquer tentativa de gravar uma linha com `TenantId` diferente do tenant
  autenticado — protege contra bug de handler, não apenas contra ataque.
- **Teste automatizado**: `tests/.../TenantIsolationTests.cs` prova que um
  tenant não enxerga registros de outro e que a gravação cross-tenant lança
  `TenantMismatchException`.

## OAuth2 (OpenIddict)

Servidor de autorização próprio, RFC 6749, endpoint único `POST
/connect/token`:

| Grant type | Uso |
|---|---|
| `client_credentials` | Comunicação serviço-a-serviço; o tenant vem de uma *property* do client OAuth. |
| `password` | Login de usuário para clientes *first-party* de confiança (SPA/mobile próprios). **Não é recomendado para clients de terceiros** — para esses, prefira Authorization Code + PKCE. |
| `refresh_token` | *Rolling refresh*: cada uso emite um novo refresh token e revoga o anterior, reduzindo o impacto de um token vazado. |

Access tokens são JWT (RSA + criptografados), carregam a claim `tenant_id`
e expiram em 15 minutos; refresh tokens expiram em 14 dias. Em
`Development`, certificados de assinatura/criptografia são efêmeros
(`AddDevelopmentSigningCertificate`); em produção, configure
`OpenIddict:SigningCertificate` / `EncryptionCertificate` com certificados
reais (variáveis de ambiente ou um secret manager — nunca em
`appsettings.json` commitado).

Aplicações OAuth e tokens ficam persistidos no Postgres (não em memória),
então qualquer instância da API atrás do load balancer processa qualquer
requisição — sem *sticky sessions*.

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

- Tenant `demo` (slug `demo`)
- Client OAuth `demo-service-client` / `demo-service-secret-change-me`
  (`client_credentials`)
- Client OAuth `demo-first-party-client` (público, `password` grant)
- Usuário `demo@demo-tenant.local` / `ChangeMe!2026#Secure`

### Obtendo um token

```bash
curl -X POST http://localhost:5080/connect/token \
  -d grant_type=password \
  -d client_id=demo-first-party-client \
  -d username=demo@demo-tenant.local \
  -d password="ChangeMe!2026#Secure" \
  -d scope=api
```

### Chamando a API

```bash
curl http://localhost:5080/api/v1/products \
  -H "Authorization: Bearer <access_token>"
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
