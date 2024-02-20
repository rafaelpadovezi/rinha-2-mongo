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
mongoSettings.ClusterConfigurator = cb =>
{
    // cb.Subscribe<CommandStartedEvent>(e =>
    // {
    //     Console.WriteLine($"{e.CommandName} - {e.Command.ToJson()}");
    // });
};
var client = new MongoClient(mongoSettings);
var collection = client.GetDatabase("rinha").GetCollection<Cliente>("clientes");

var app = builder.Build();

app.MapGet("/", () => "Hello World!");

app.MapGet("/clientes/{id:int}/extrato", async (int id) =>
{
    var filter = Builders<Cliente>.Filter.Eq(nameof(Cliente.Id), id);
    var projection = Builders<Cliente>.Projection
        .Include(nameof(Cliente.Saldo))
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
            Total = cliente.Saldo,
            Limite = cliente.Limite
        },
        UltimasTransacoes = new List<TransacaoDto>(cliente.Transacoes.Count)
    };
    foreach (var transacao in cliente.Transacoes)
    {
        extratoDto.UltimasTransacoes.Add(new TransacaoDto
        {
            Valor = transacao.Valor,
            Tipo = transacao.Tipo,
            Descricao = transacao.Descricao,
            RealizadoEm = transacao.RealizadoEm
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
                 & Builders<Cliente>.Filter.Gte(nameof(Cliente.Saldo), cliente.Limite * -1 - valorTransacao);
    var update = Builders<Cliente>.Update
        .Inc(nameof(Cliente.Saldo), valorTransacao)
        .PushEach(x => x.Transacoes,
            new[] { new Transacao { Valor = valor, Tipo = transacao.Tipo, Descricao = transacao.Descricao } },
            slice: 10, position: 0, sort: null);
    var result = await collection.FindOneAndUpdateAsync(filter, update,
        new FindOneAndUpdateOptions<Cliente> { ReturnDocument = ReturnDocument.After });
    if (result == null)
        return Results.UnprocessableEntity("Saldo insuficiente");
    return Results.Ok(new ResultadoSaldo(result.Saldo, result.Limite));
});

app.Run();

[JsonSerializable(typeof(ExtratoDto))]
[JsonSerializable(typeof(SaldoDto))]
[JsonSerializable(typeof(ResultadoSaldo))]
[JsonSerializable(typeof(TransacaoDto))]
[JsonSerializable(typeof(TransacaoRequestDto))]
internal partial class AppJsonSerializerContext : JsonSerializerContext {}