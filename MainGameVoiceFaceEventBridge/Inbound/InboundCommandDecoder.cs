namespace MainGameVoiceFaceEventBridge.Inbound
{
    internal sealed class InboundCommandDecoder
    {
        internal bool TryDecode(
            string line,
            PluginSettings settings,
            out ExternalVoiceFaceCommand command,
            out string reason)
        {
            string value = (line ?? string.Empty).Trim();
            if (value.StartsWith("{", System.StringComparison.Ordinal)
                && (settings == null || settings.AcceptJsonCommand)
                && !InboundJsonValidator.TryValidateObject(value, out reason))
            {
                command = null;
                return false;
            }

            return CommandParser.TryParseIncoming(line, settings, out command, out reason);
        }
    }
}
