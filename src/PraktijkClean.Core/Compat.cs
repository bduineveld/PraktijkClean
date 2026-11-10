using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

// PraktijkClean draait op .NET Framework 4.8, dat in elke Windows 10 en 11 zit. Daardoor hoeft er geen
// .NET-runtime in de exe te zitten (de exe is enkele honderden kB in plaats van ruim 100 MB).
// Dit bestand vult de paar onderdelen aan die .NET Framework 4.8 mist ten opzichte van nieuwere .NET-versies.

namespace System.Runtime.CompilerServices
{
    /// <summary>Nodig voor C#-records en 'init' op .NET Framework.</summary>
    internal static class IsExternalInit { }
}

namespace System.Runtime.Versioning
{
    /// <summary>Alleen documentatie op .NET Framework: markeert Windows-specifieke code.</summary>
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = false)]
    internal sealed class SupportedOSPlatformAttribute : Attribute
    {
        public SupportedOSPlatformAttribute(string platformName) { PlatformName = platformName; }
        public string PlatformName { get; }
    }
}

namespace PraktijkClean.Core
{
    internal static class StringCompat
    {
        public static bool StartsWith(this string s, char c) => s.Length > 0 && s[0] == c;
        public static bool EndsWith(this string s, char c) => s.Length > 0 && s[s.Length - 1] == c;
        public static bool Contains(this string s, char c) => s.IndexOf(c) >= 0;
        public static bool Contains(this string s, string value, StringComparison comparison) => s.IndexOf(value, comparison) >= 0;
    }

    /// <summary>
    /// JSON lezen en schrijven met de ingebouwde DataContractJsonSerializer (geen extra DLL's nodig).
    /// Klassen die zo worden gelezen zetten hun standaardwaarden in een [OnDeserializing]-methode,
    /// omdat de serializer geen constructor aanroept.
    /// </summary>
    public static class Json
    {
        private static readonly DataContractJsonSerializerSettings Settings = new() { UseSimpleDictionaryFormat = true };

        public static string Serialize<T>(T value)
        {
            using var ms = new MemoryStream();
            using (var writer = JsonReaderWriterFactory.CreateJsonWriter(ms, new UTF8Encoding(false), ownsStream: false, indent: true, indentChars: "  "))
            {
                new DataContractJsonSerializer(typeof(T), Settings).WriteObject(writer, value);
                writer.Flush();
            }
            return Encoding.UTF8.GetString(ms.ToArray());
        }

        /// <summary>Gooit <see cref="JsonFormatException"/> bij ongeldige of lege JSON.</summary>
        public static T Deserialize<T>(string json) where T : class
        {
            if (string.IsNullOrWhiteSpace(json)) throw new JsonFormatException("Lege JSON.");
            // DataContractJsonSerializer maakt van bv. [1,2] stilzwijgend een leeg object; dat willen we niet.
            if (json.TrimStart('\uFEFF', ' ', '\t', '\r', '\n')[0] != '{') throw new JsonFormatException("JSON is geen object.");
            try
            {
                using var ms = new MemoryStream(Encoding.UTF8.GetBytes(json));
                return new DataContractJsonSerializer(typeof(T), Settings).ReadObject(ms) as T
                       ?? throw new JsonFormatException("Leeg resultaat.");
            }
            catch (Exception ex) when (ex is SerializationException or System.Xml.XmlException or InvalidCastException or FormatException)
            {
                throw new JsonFormatException("Ongeldige JSON (" + ex.GetType().Name + ").", ex);
            }
        }
    }

    public sealed class JsonFormatException : Exception
    {
        public JsonFormatException(string message, Exception? inner = null) : base(message, inner) { }
    }

    internal static class FileCompat
    {
        /// <summary>Vervangt het doelbestand door het tijdelijke bestand (zoals File.Move met overwrite).</summary>
        public static void MoveOverwrite(string source, string destination)
        {
            if (File.Exists(destination)) File.Replace(source, destination, null);
            else File.Move(source, destination);
        }
    }
}
