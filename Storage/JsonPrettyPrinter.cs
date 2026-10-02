using System.IO;
using System.Text;
using Landoria.WorldCrawler.Restoration.Persistence;

namespace Landoria.WorldCrawler.Storage
{
    // Formats serialized JSON without loading large zone files into memory.
    internal sealed class JsonPrettyPrinter
    {
        private readonly TextWriter _writer;
        private int _depth;
        private bool _quoted, _escaped;

        // Keeps formatting separate from the serializer's bounded temporary file.
        private JsonPrettyPrinter(TextWriter writer)
        {
            _writer = writer;
        }

        // Streams UTF-8 JSON into a human-readable committed candidate.
        internal static void Format(string source, string destination)
        {
            using (var reader = new StreamReader(source, Encoding.UTF8, true))
            using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(output, new UTF8Encoding(false)))
            {
                writer.NewLine = "\n";
                var printer = new JsonPrettyPrinter(writer);
                int value;
                while ((value = reader.Read()) != -1)
                {
                    printer.Write((char)value);
                }
                writer.Flush();
                output.Flush(true);
            }
        }

        // Adds whitespace only outside quoted JSON strings.
        private void Write(char value)
        {
            if (_quoted)
            {
                _writer.Write(value);
                if (_escaped)
                {
                    _escaped = false;
                }
                else if (value == '\\')
                {
                    _escaped = true;
                }
                else if (value == '"')
                {
                    _quoted = false;
                }
                return;
            }
            if (char.IsWhiteSpace(value))
            {
                return;
            }
            if (value == '"')
            {
                _quoted = true;
                _writer.Write(value);
                return;
            }
            WriteStructural(value);
        }

        // Formats punctuation and copies scalar values without changing their bytes.
        private void WriteStructural(char value)
        {
            if (value == '{' || value == '[')
            {
                _writer.Write(value);
                _depth++;
                NewLine();
                return;
            }
            if (value == '}' || value == ']')
            {
                _depth--;
                NewLine();
                _writer.Write(value);
                return;
            }
            if (value == ',')
            {
                _writer.Write(value);
                NewLine();
                return;
            }
            _writer.Write(value);
            if (value == ':')
            {
                _writer.Write(' ');
            }
        }

        // Indents each nested object or array consistently.
        private void NewLine()
        {
            _writer.WriteLine();
            for (var level = 0; level < _depth; level++)
            {
                _writer.Write("  ");
            }
        }
    }
}
