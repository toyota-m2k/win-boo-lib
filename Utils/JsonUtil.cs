using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace io.github.toyota32k.BooLib.Utils; 
public static class JsonUtil {
    public static readonly JsonNodeOptions NodeCaseInsensitive = new JsonNodeOptions() { PropertyNameCaseInsensitive = true };
    public static readonly JsonSerializerOptions SerializeCaseInsensitive = new JsonSerializerOptions() { PropertyNameCaseInsensitive = true };

    public static JsonObject? Parse(string? jsonString, JsonNodeOptions? option=null) {
        try {
            if (jsonString == null) return null;
            return JsonNode.Parse(jsonString, option)?.AsObject();
        } catch(Exception) {
            return null;
        }
    }

    public static JsonObject ParseOrThrow(string? jsonString, JsonNodeOptions? option = null) {
        if (jsonString == null) throw new ArgumentNullException("no json");
        var node = JsonNode.Parse(jsonString);
        if (node == null) {
            throw new InvalidOperationException("cannot parse json");
        }
        return node.AsObject();
    }

    #region Long

    public static long GetLongOrThrow(this JsonObject? j, string key) {
        if (j == null) throw new ArgumentNullException(key);
        var v = j[key]?.GetValue<long>();
        if (v==null) {
            throw new KeyNotFoundException(key);
        }
        return v.Value;
    }

    public static long? GetLongOrNull(this JsonObject? j, string key) {
        try {
            if (j == null) return null;
            return j[key]?.GetValue<long>();
        }
        catch (Exception) {
            return null;
        }
    }

    public static long GetLong(this JsonObject? j, string key, long def=0) {
        return GetIntOrNull(j, key) ?? def;
    }

    #endregion

    #region Int

    public static int GetIntOrThrow(this JsonObject? j, string key) {
        return (int)GetLongOrThrow(j, key);
    }

    public static int? GetIntOrNull(this JsonObject? j, string key) {
        return (int?)GetLongOrNull(j, key);
    }

    public static int GetInt(this JsonObject? j, string key, int def = 0) {
        return GetIntOrNull(j, key) ?? def;
    }

    #endregion

    #region Boolean

    public static bool GetBoolOrThrow(this JsonObject? j, string key) {
        if (j == null) throw new ArgumentNullException("key");
        var v = j[key]?.GetValue<bool>();
        if (v == null) {
            throw new KeyNotFoundException("key");
        }
        return v.Value;
    }

    public static bool? GetBoolOrNull(this JsonObject? j, string key) {
        try {
            if (j == null) return null;
            return j[key]?.GetValue<bool>();
        }
        catch (Exception) {
            return null;
        }
    }
    public static bool GetBool(this JsonObject? j, string key, bool def=false) {
        return GetBoolOrNull(j, key) ?? def;
    }

    #endregion

    #region String

    public static string GetStringOrThrow(this JsonObject? j, string key) {
        if (j == null) throw new ArgumentNullException(key);
        var v = j[key]?.GetValue<string>();
        if (v == null) {
            throw new KeyNotFoundException(key);
        }
        return v;
    }
    public static string? GetStringOrNull(this JsonObject? j, string key) {
        try {
            if (j == null) return null;
            return j[key]?.GetValue<string>();
        }
        catch (Exception) {
            return null;
        }
    }
    public static string GetString(this JsonObject? j, string key, string def) {
        return GetStringOrNull(j, key) ?? def;
    }

    #endregion
}

public class NumberOrStringConverter : JsonConverter<string> {
    public override string Read(ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert, System.Text.Json.JsonSerializerOptions options) {
        return reader.TokenType switch {
            System.Text.Json.JsonTokenType.Number => reader.TryGetInt64(out var l) ? l.ToString() : reader.GetDouble().ToString(System.Globalization.CultureInfo.InvariantCulture),
            System.Text.Json.JsonTokenType.String => reader.GetString() ?? "",
            System.Text.Json.JsonTokenType.Null => "",
            _ => throw new System.Text.Json.JsonException($"Unexpected token {reader.TokenType} for id"),
        };
    }
    public override void Write(System.Text.Json.Utf8JsonWriter writer, string value, System.Text.Json.JsonSerializerOptions options) {
        writer.WriteStringValue(value);
    }
}
