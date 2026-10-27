using System;

namespace MainGameBlankMapAdd.Http
{
    internal sealed class PlaybackHttpCommand
    {
        internal int? Index { get; set; }
        internal string Filename { get; set; }
    }

    internal static class HttpCommandParser
    {
        internal static PlaybackHttpCommand ParsePlaybackCommand(string body)
        {
            string source = body ?? string.Empty;
            var command = new PlaybackHttpCommand();

            int indexPosition = source.IndexOf("\"index\"", StringComparison.OrdinalIgnoreCase);
            if (indexPosition >= 0)
            {
                int colon = source.IndexOf(':', indexPosition);
                if (colon >= 0)
                {
                    string rest = source.Substring(colon + 1).TrimStart();
                    int end = 0;
                    while (end < rest.Length && (char.IsDigit(rest[end]) || rest[end] == '-')) end++;
                    if (end > 0 && int.TryParse(rest.Substring(0, end), out int parsed))
                        command.Index = parsed;
                }
            }

            int filenamePosition = source.IndexOf("\"filename\"", StringComparison.OrdinalIgnoreCase);
            if (filenamePosition >= 0)
            {
                int colon = source.IndexOf(':', filenamePosition);
                int firstQuote = colon < 0 ? -1 : source.IndexOf('"', colon + 1);
                int secondQuote = firstQuote < 0 ? -1 : source.IndexOf('"', firstQuote + 1);
                if (secondQuote > firstQuote)
                    command.Filename = source.Substring(firstQuote + 1, secondQuote - firstQuote - 1);
            }

            return command;
        }
    }
}
