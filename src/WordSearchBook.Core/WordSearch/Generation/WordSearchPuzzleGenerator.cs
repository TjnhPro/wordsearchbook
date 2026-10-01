using WordSearchBook.Core.WordSearch.Contracts;
using WordSearchBook.Core.WordSearch.Domain;
using WordSearchHelper;

namespace WordSearchBook.Core.WordSearch.Generation;

public sealed class WordSearchPuzzleGenerator : IWordSearchPuzzleGenerator
{
    private const int MaximumAttempts = 3;

    public WordSearchPuzzle Generate(IReadOnlyList<string> wordSearchKeys, BoardSize boardSize)
    {
        ArgumentNullException.ThrowIfNull(wordSearchKeys);
        ArgumentNullException.ThrowIfNull(boardSize);

        if (wordSearchKeys.Count == 0)
        {
            throw new WordSearchGenerationException("word_list_empty", "At least one word search key is required.");
        }

        if (boardSize.Width <= 0 || boardSize.Height <= 0)
        {
            throw new WordSearchGenerationException("board_size_invalid", "Board width and height must be positive.");
        }

        var words = wordSearchKeys.ToArray();
        if (words.Any(string.IsNullOrWhiteSpace))
        {
            throw new WordSearchGenerationException(
                "word_invalid",
                "Word Search Key is empty. Enter letters A-Z.");
        }

        var maximumLength = Math.Min(boardSize.Width, boardSize.Height);
        if (words.FirstOrDefault(word => word.Length > maximumLength) is { } oversizedWord)
        {
            throw new WordSearchGenerationException(
                "word_too_long",
                $"Word Search Key '{oversizedWord}' has {oversizedWord.Length} letters; the maximum is {maximumLength}. Shorten the Word Search Key.");
        }

        Exception? lastException = null;
        for (var attempt = 1; attempt <= MaximumAttempts; attempt++)
        {
            try
            {
                var wordBank = new WordBank(words, uppercase: true);
                var data = wordBank.Generate(boardSize.Width, boardSize.Height);
                var answer = wordBank.AnswerKey();
                var placements = wordBank.answers
                    .Select(letters => new WordPlacement(
                        new string(letters.Select(letter => letter._Letter).ToArray()),
                        letters.Select(letter => new GridPoint(letter.X, letter.Y)).ToArray()))
                    .ToArray();

                return new WordSearchPuzzle(data, answer, placements);
            }
            catch (Exception exception)
            {
                lastException = exception;
            }
        }

        throw new WordSearchGenerationException(
            "puzzle_generation_failed",
            $"Could not place {words.Length} words on a {boardSize.Width}x{boardSize.Height} board after {MaximumAttempts} attempts.",
            lastException);
    }
}
