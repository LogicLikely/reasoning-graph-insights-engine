using System.Buffers;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Backend.Insights.Contracts;

public static class CanonicalJson
{
    private static readonly JsonSerializerOptions DefaultSerializerOptions = CreateSerializerOptions();

    public static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DictionaryKeyPolicy = null,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            WriteIndented = false
        };
        options.Converters.Add(new StrictKebabCaseEnumConverterFactory());
        return options;
    }

    public static string Canonicalize(JsonElement value)
    {
        return Encoding.UTF8.GetString(CanonicalizeToUtf8(value));
    }

    public static string Canonicalize<T>(T value, JsonSerializerOptions? options = null)
    {
        var element = JsonSerializer.SerializeToElement(value, options ?? DefaultSerializerOptions);
        return Canonicalize(element);
    }

    public static string ComputeSha256(JsonElement value)
    {
        return ComputeSha256(CanonicalizeToUtf8(value));
    }

    public static string ComputeSha256<T>(T value, JsonSerializerOptions? options = null)
    {
        var element = JsonSerializer.SerializeToElement(value, options ?? DefaultSerializerOptions);
        return ComputeSha256(element);
    }

    private static byte[] CanonicalizeToUtf8(JsonElement value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            Indented = false,
            SkipValidation = false
        });

        WriteCanonicalValue(writer, value);
        writer.Flush();
        return buffer.WrittenSpan.ToArray();
    }

    private static void WriteCanonicalValue(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                var properties = value.EnumerateObject()
                    .OrderBy(property => property.Name, StringComparer.Ordinal)
                    .ToArray();
                if (properties.Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() != properties.Length)
                {
                    throw new FormatException("Canonical JSON objects cannot contain duplicate member names.");
                }

                foreach (var property in properties)
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonicalValue(writer, property.Value);
                }

                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray())
                {
                    WriteCanonicalValue(writer, item);
                }

                writer.WriteEndArray();
                break;

            case JsonValueKind.String:
                writer.WriteStringValue(value.GetString());
                break;

            case JsonValueKind.Number:
                writer.WriteRawValue(NormalizeJsonNumber(value.GetRawText()), skipInputValidation: false);
                break;

            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;

            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;

            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;

            default:
                throw new FormatException($"JSON value kind '{value.ValueKind}' is not canonicalizable.");
        }
    }

    private static string NormalizeJsonNumber(string rawNumber)
    {
        var span = rawNumber.AsSpan();
        var index = 0;
        var negative = false;
        if (span[index] == '-')
        {
            negative = true;
            index++;
        }

        var integerStart = index;
        while (index < span.Length && char.IsAsciiDigit(span[index]))
        {
            index++;
        }

        var integerDigits = span[integerStart..index];
        ReadOnlySpan<char> fractionalDigits = [];
        if (index < span.Length && span[index] == '.')
        {
            index++;
            var fractionStart = index;
            while (index < span.Length && char.IsAsciiDigit(span[index]))
            {
                index++;
            }

            fractionalDigits = span[fractionStart..index];
        }

        var explicitExponent = BigInteger.Zero;
        if (index < span.Length && (span[index] == 'e' || span[index] == 'E'))
        {
            index++;
            var exponentNegative = false;
            if (index < span.Length && (span[index] == '+' || span[index] == '-'))
            {
                exponentNegative = span[index] == '-';
                index++;
            }

            var exponentDigits = span[index..];
            explicitExponent = BigInteger.Parse(exponentDigits, NumberStyles.None, CultureInfo.InvariantCulture);
            if (exponentNegative)
            {
                explicitExponent = -explicitExponent;
            }

            index = span.Length;
        }

        if (index != span.Length || integerDigits.IsEmpty)
        {
            throw new FormatException($"Invalid JSON number '{rawNumber}'.");
        }

        var coefficient = string.Concat(integerDigits, fractionalDigits);
        var firstNonZero = 0;
        while (firstNonZero < coefficient.Length && coefficient[firstNonZero] == '0')
        {
            firstNonZero++;
        }

        if (firstNonZero == coefficient.Length)
        {
            return "0";
        }

        coefficient = coefficient[firstNonZero..];
        var scale = explicitExponent - fractionalDigits.Length;
        var lastNonZero = coefficient.Length - 1;
        while (lastNonZero > 0 && coefficient[lastNonZero] == '0')
        {
            lastNonZero--;
            scale++;
        }

        coefficient = coefficient[..(lastNonZero + 1)];

        var scientific = BuildScientificNumber(coefficient, scale, negative);
        var plainLength = GetPlainNumberLength(coefficient.Length, scale, negative);
        if (plainLength <= scientific.Length && plainLength <= int.MaxValue)
        {
            return BuildPlainNumber(coefficient, scale, negative);
        }

        return scientific;
    }

    private static string BuildScientificNumber(string coefficient, BigInteger scale, bool negative)
    {
        var exponent = scale + coefficient.Length - 1;
        var mantissa = coefficient.Length == 1
            ? coefficient
            : string.Concat(coefficient.AsSpan(0, 1), ".", coefficient.AsSpan(1));
        var sign = negative ? "-" : string.Empty;
        return exponent.IsZero
            ? string.Concat(sign, mantissa)
            : string.Concat(sign, mantissa, "e", exponent.ToString(CultureInfo.InvariantCulture));
    }

    private static BigInteger GetPlainNumberLength(int coefficientLength, BigInteger scale, bool negative)
    {
        var signLength = negative ? BigInteger.One : BigInteger.Zero;
        if (scale.Sign >= 0)
        {
            return signLength + coefficientLength + scale;
        }

        var decimalPosition = coefficientLength + scale;
        return decimalPosition.Sign > 0
            ? signLength + coefficientLength + 1
            : signLength + 2 - decimalPosition + coefficientLength;
    }

    private static string BuildPlainNumber(string coefficient, BigInteger scale, bool negative)
    {
        var sign = negative ? "-" : string.Empty;
        if (scale.Sign >= 0)
        {
            return string.Concat(sign, coefficient, new string('0', (int)scale));
        }

        var decimalPosition = coefficient.Length + (int)scale;
        if (decimalPosition > 0)
        {
            return string.Concat(
                sign,
                coefficient.AsSpan(0, decimalPosition),
                ".",
                coefficient.AsSpan(decimalPosition));
        }

        return string.Concat(sign, "0.", new string('0', -decimalPosition), coefficient);
    }

    private static string ComputeSha256(byte[] canonicalUtf8)
    {
        var hash = SHA256.HashData(canonicalUtf8);
        return $"sha256:{Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    private sealed class StrictKebabCaseEnumConverterFactory : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

        public override JsonConverter CreateConverter(
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            var converterType = typeof(StrictKebabCaseEnumConverter<>).MakeGenericType(typeToConvert);
            return (JsonConverter)Activator.CreateInstance(converterType)!;
        }
    }

    private sealed class StrictKebabCaseEnumConverter<TEnum> : JsonConverter<TEnum>
        where TEnum : struct, Enum
    {
        private static readonly IReadOnlyDictionary<string, TEnum> ValuesByToken =
            Enum.GetValues<TEnum>().ToDictionary(
                value => JsonNamingPolicy.KebabCaseLower.ConvertName(value.ToString()),
                value => value,
                StringComparer.Ordinal);

        public override TEnum Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String ||
                !ValuesByToken.TryGetValue(reader.GetString() ?? string.Empty, out var value))
            {
                throw new JsonException(
                    $"Expected an exact kebab-case {typeof(TEnum).Name} token.");
            }

            return value;
        }

        public override void Write(
            Utf8JsonWriter writer,
            TEnum value,
            JsonSerializerOptions options)
        {
            var token = ValuesByToken.FirstOrDefault(entry =>
                EqualityComparer<TEnum>.Default.Equals(entry.Value, value));
            if (token.Key is null)
            {
                throw new JsonException(
                    $"Value '{value}' is not a defined {typeof(TEnum).Name} contract value.");
            }

            writer.WriteStringValue(token.Key);
        }
    }
}
