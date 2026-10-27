using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace StudioFacePresetTool
{
    // Minimal JSON parser (public domain style) for dictionaries/lists/primitives.
    internal static class MiniJson
    {
        public static object Deserialize(string json)
        {
            if (json == null)
            {
                return null;
            }
            return new Parser(json).ParseValue();
        }

        private sealed class Parser
        {
            private readonly string _json;
            private int _index;

            public Parser(string json)
            {
                _json = json;
                _index = 0;
            }

            public object ParseValue()
            {
                EatWhitespace();
                if (_index >= _json.Length)
                {
                    return null;
                }

                char c = _json[_index];
                switch (c)
                {
                    case '{': return ParseObject();
                    case '[': return ParseArray();
                    case '"': return ParseString();
                    case 't': return ParseLiteral("true", true);
                    case 'f': return ParseLiteral("false", false);
                    case 'n': return ParseLiteral("null", null);
                    default: return ParseNumber();
                }
            }

            private Dictionary<string, object> ParseObject()
            {
                var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                _index++; // {
                while (true)
                {
                    EatWhitespace();
                    if (_index >= _json.Length)
                    {
                        return dict;
                    }
                    if (_json[_index] == '}')
                    {
                        _index++;
                        return dict;
                    }

                    string key = ParseString();
                    EatWhitespace();
                    if (_index < _json.Length && _json[_index] == ':')
                    {
                        _index++;
                    }
                    object value = ParseValue();
                    dict[key] = value;
                    EatWhitespace();
                    if (_index < _json.Length && _json[_index] == ',')
                    {
                        _index++;
                        continue;
                    }
                    if (_index < _json.Length && _json[_index] == '}')
                    {
                        _index++;
                        return dict;
                    }
                }
            }

            private List<object> ParseArray()
            {
                var list = new List<object>();
                _index++; // [
                while (true)
                {
                    EatWhitespace();
                    if (_index >= _json.Length)
                    {
                        return list;
                    }
                    if (_json[_index] == ']')
                    {
                        _index++;
                        return list;
                    }
                    object value = ParseValue();
                    list.Add(value);
                    EatWhitespace();
                    if (_index < _json.Length && _json[_index] == ',')
                    {
                        _index++;
                        continue;
                    }
                    if (_index < _json.Length && _json[_index] == ']')
                    {
                        _index++;
                        return list;
                    }
                }
            }

            private string ParseString()
            {
                var sb = new StringBuilder();
                if (_json[_index] == '"')
                {
                    _index++;
                }
                while (_index < _json.Length)
                {
                    char c = _json[_index++];
                    if (c == '"')
                    {
                        break;
                    }
                    if (c == '\\' && _index < _json.Length)
                    {
                        char esc = _json[_index++];
                        switch (esc)
                        {
                            case '"': sb.Append('"'); break;
                            case '\\': sb.Append('\\'); break;
                            case '/': sb.Append('/'); break;
                            case 'b': sb.Append('\b'); break;
                            case 'f': sb.Append('\f'); break;
                            case 'n': sb.Append('\n'); break;
                            case 'r': sb.Append('\r'); break;
                            case 't': sb.Append('\t'); break;
                            case 'u':
                                if (_index + 4 <= _json.Length)
                                {
                                    string hex = _json.Substring(_index, 4);
                                    _index += 4;
                                    if (ushort.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
                                    {
                                        sb.Append((char)code);
                                    }
                                }
                                break;
                            default:
                                sb.Append(esc);
                                break;
                        }
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
                return sb.ToString();
            }

            private object ParseNumber()
            {
                int start = _index;
                while (_index < _json.Length)
                {
                    char c = _json[_index];
                    if (!char.IsDigit(c) && c != '-' && c != '+' && c != '.' && c != 'e' && c != 'E')
                    {
                        break;
                    }
                    _index++;
                }
                string num = _json.Substring(start, _index - start);
                if (double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                {
                    return d;
                }
                return 0d;
            }

            private object ParseLiteral(string literal, object value)
            {
                if (_json.Length >= _index + literal.Length
                    && string.Compare(_json, _index, literal, 0, literal.Length, StringComparison.Ordinal) == 0)
                {
                    _index += literal.Length;
                    return value;
                }
                return null;
            }

            private void EatWhitespace()
            {
                while (_index < _json.Length && char.IsWhiteSpace(_json[_index]))
                {
                    _index++;
                }
            }
        }
    }
}
