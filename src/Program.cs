using System.Text.Json;
using System.Text.Json.Serialization;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Core.Events;
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
mongoSettings.ClusterConfigurator = cb =>
{
    cb.Subscribe<CommandStartedEvent>(e =>
    {
        Console.WriteLine($"{e.CommandName} - {e.Command.ToJson()}");
    });
};
var client = new MongoClient(mongoSettings);
var collection = client.GetDatabase("rinha").GetCollection<Cliente>("clientes");

var app = builder.Build();

app.MapGet("/", () => "Hello World!");

app.MapGet("/clientes/{id:int}/extrato", async (int id) =>
{
    var filter = Builders<Cliente>.Filter.Eq(nameof(Cliente.Id), id);
    var projection = Builders<Cliente>.Projection
        .Include(nameof(Cliente.Total))
        .Include(nameof(Cliente.Limite))
        .Slice(nameof(Cliente.Transacoes), 10);
    var cliente = await collection.Find(filter).Project<Cliente?>(projection)
        .SingleOrDefaultAsync();
    if (cliente == null)
        return Results.NotFound();
    var extratoDto = new ExtratoDto
    {
        Saldo = new SaldoDto
        {
            Total = cliente.Total,
            Limite = cliente.Limite
        },
        UltimasTransacoes = new List<TransacaoDto>(cliente.Transacoes.Count)
    };
    foreach (var transacao in cliente.Transacoes)
    {
        extratoDto.UltimasTransacoes.Insert(0, new TransacaoDto
        {
            Valor = transacao.Valor,
            Tipo = transacao.Tipo,
            Descricao = transacao.Descricao,
        });
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

    var cliente = await collection
        .Find(Builders<Cliente>.Filter.Eq(nameof(Cliente.Id), id))
        .Project<Cliente?>(Builders<Cliente>.Projection
            .Include(nameof(Cliente.Limite)))
        .SingleOrDefaultAsync();
    if (cliente == null)
        return Results.NotFound();
    var valorTransacao = transacao.Tipo == 'c' ? valor : valor * -1;

    var filter = Builders<Cliente>.Filter
                     .Eq(nameof(Cliente.Id), id)
                 & Builders<Cliente>.Filter.Gte(x => x.Total + valorTransacao, cliente.Limite * 1);
    var update = Builders<Cliente>.Update(x => x.Total, x => x.Total + valorTransacao)
        .Push(x => x.Transacoes, new Transacao
        {
            Valor = valorTransacao,
            Tipo = transacao.Tipo,
            Descricao = transacao.Descricao
        });
});

app.Run();

[JsonSerializable(typeof(ExtratoDto))]
[JsonSerializable(typeof(SaldoDto))]
[JsonSerializable(typeof(ResultadoSaldo))]
[JsonSerializable(typeof(TransacaoDto))]
[JsonSerializable(typeof(TransacaoRequestDto))]
internal partial class AppJsonSerializerContext : JsonSerializerContext {}