namespace WordSearchBook.Core.WordSearch.Contracts;

public sealed class WordSearchGenerationException : Exception
{
    public WordSearchGenerationException(string code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    public string Code { get; }
}
