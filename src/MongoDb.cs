using System.Collections.Concurrent;

using MongoDB.Bson;
using MongoDB.Driver;

namespace RinhaMongo;

public class MongoDb
{
    private static readonly ConcurrentDictionary<int, int?> LimiteCache = new ();
    private readonly static FindOneAndUpdateOptions<Cliente> findOneAndUpdateOptions = new()
    {
        ReturnDocument = ReturnDocument.After,
        Projection = Builders<Cliente>.Projection
            .Include("Saldo")
            .Include("Limite")
    };

    private readonly static ProjectionDefinition<Cliente> projectExtrato = Builders<Cliente>.Projection
        .Include("Saldo")
        .Include("Limite")
        .Slice("Transacoes", 10);
    private readonly static ProjectionDefinition<Cliente> projectLimite = Builders<Cliente>.Projection
        .Include("Limite")
        .Exclude("_id");
    
    private readonly IMongoCollection<Cliente> _collection;
    private readonly IMongoCollection<RawBsonDocument> _collectionRaw;

    public MongoDb(IMongoClient client)
    {
        _collection = client.GetDatabase("rinha").GetCollection<Cliente>("clientes");
        _collectionRaw = client.GetDatabase("rinha").GetCollection<RawBsonDocument>("clientes");
    }

    public Task<Cliente?> GetExtratoClienteAsync(int id) =>
        _collection
            .Find(Builders<Cliente>.Filter.Eq("Id", id))
            .Project<Cliente?>(projectExtrato)
            .SingleOrDefaultAsync();

    public async Task<int?> GetLimiteClienteAsync(int id)
    {
        if (LimiteCache.TryGetValue(id, out var limite))
            return limite;
        
        limite =  await GetLimiteFromDbAsync();
        LimiteCache.TryAdd(id, limite);
        return limite;

        async Task<int?> GetLimiteFromDbAsync() =>
            (await _collection
                .Find(Builders<Cliente>.Filter.Eq("Id", id))
                .Project<Cliente?>(projectLimite)
                .SingleOrDefaultAsync())?.Limite;
    }

    public Task<Cliente?> FindAndUpdateSaldoAsync(int id, TransacaoRequestDto transacao, int limite, int valor)
    {
        int valorTransacao;
        FilterDefinition<Cliente> filter;
        if (transacao.Tipo == 'c')
        {
            valorTransacao = valor;
            filter = Builders<Cliente>.Filter.Eq("Id", id);
        }
        else
        {
            valorTransacao = -valor;
            filter = Builders<Cliente>.Filter.Eq("Id", id) &
                     Builders<Cliente>.Filter.Gte("Saldo", limite * -1 - valorTransacao);
        }
        var update = Builders<Cliente>.Update
            .Inc("Saldo", valorTransacao)
            .PushEach(x => x.Transacoes,
                new[]
                {
                    new Transacao
                    {
                        Valor = valor,
                        Tipo = transacao.Tipo,
                        Descricao = transacao.Descricao
                    }
                },
                slice: -10, position: null, sort: null);
        return _collection.FindOneAndUpdateAsync(
            filter,
            update,
            findOneAndUpdateOptions);
    }
}