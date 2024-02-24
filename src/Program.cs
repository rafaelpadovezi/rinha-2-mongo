using System.Text.Json;
using System.Text.Json.Serialization;

using MongoDB.Driver;

using RinhaMongo;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
});

var mongoSettings = MongoClientSettings.FromConnectionString(
    builder.Configuration.GetConnectionString("MongoDb") ??
    throw new InvalidOperationException("MongoDb connection string not found"));
var client = new MongoClient(mongoSettings);
var mongoDb = new MongoDb(client);

var app = builder.Build();

app.MapGet("/", () => "It's up!");

app.MapGet("/clientes/{id:int}/extrato", async (int id) =>
{
    var cliente = await mongoDb.GetExtratoClienteAsync(id);
    if (cliente == null)
        return Results.NotFound();
    var extratoDto = new ExtratoDto
    {
        Saldo = new SaldoDto
        {
            Total = cliente.Saldo,
            Limite = cliente.Limite
        },
        UltimasTransacoes = new TransacaoDto[cliente.Transacoes.Count]
    };
    for (var i = 0; i < cliente.Transacoes.Count; i++)
    {
        var transacao = cliente.Transacoes[i];
        extratoDto.UltimasTransacoes[cliente.Transacoes.Count - i - 1] = new TransacaoDto
        {
            Valor = transacao.Valor,
            Tipo = transacao.Tipo,
            Descricao = transacao.Descricao,
            RealizadoEm = transacao.RealizadoEm
        };
    }

    return Results.Ok(extratoDto);
});

app.MapPost("/clientes/{id:int}/transacoes", async (int id, TransacaoRequestDto transacao) =>
{
    if (transacao.Tipo != 'c' && transacao.Tipo != 'd')
        return Results.UnprocessableEntity("Tipo inválido");
    if (!int.TryParse(transacao.Valor?.ToString(), out var valor))
        return Results.UnprocessableEntity("Valor inválido");
    if (string.IsNullOrEmpty(transacao.Descricao) || transacao.Descricao.Length > 10)
        return Results.UnprocessableEntity("Descrição inválida");

    var limite = await mongoDb.GetLimiteClienteAsync(id);
    if (limite == null)
        return Results.NotFound();

    var result = await mongoDb.FindAndUpdateSaldoAsync(id, transacao, limite.Value, valor);
    return result != null
        ? Results.Ok(new ResultadoSaldo(result.Saldo, result.Limite))
        : Results.UnprocessableEntity("Saldo insuficiente");
});

app.Run();

[JsonSerializable(typeof(ExtratoDto))]
[JsonSerializable(typeof(SaldoDto))]
[JsonSerializable(typeof(ResultadoSaldo))]
[JsonSerializable(typeof(TransacaoDto))]
[JsonSerializable(typeof(TransacaoRequestDto))]
internal partial class AppJsonSerializerContext : JsonSerializerContext;