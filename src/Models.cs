namespace RinhaMongo;

public class Cliente
{
    public int Id { get; set; }
    public int Total { get; set; }
    public int Limite { get; set; }
    public List<Transacao> Transacoes { get; set; }
}

public class Transacao
{
    public int Valor { get; set; }
    public char Tipo { get; set; }
    public string Descricao { get; set; }
    public DateTime RealizadoEm { get; set; } = DateTime.Now;
}