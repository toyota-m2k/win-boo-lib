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
        var r = GetLongOrNull(j, key);
        if (r == null) {
            throw new KeyNotFoundException(key);
        }
        return r.Value;
    }

    public static long? GetLongOrNull(this JsonObject? j, string key) {
        try {
            if (j?[key] is not JsonValue v) return null;
            return v.GetValueKind() switch {
                JsonValueKind.String => long.Parse(v.GetValue<string>()),
                JsonValueKind.Number => v.GetValue<long>(),
                JsonValueKind.True => 1,
                JsonValueKind.False => 0,
                _ => null,
            };
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
        var r = GetBoolOrNull(j, key);
        if (r == null) {
            throw new KeyNotFoundException(key);
        }
        return r.Value;
    }

    private static bool string2bool(string s) {
        if (long.TryParse(s, out var l)) {
            return l != 0;
        }
        if (bool.TryParse(s, out var b)) {
            return b;
        }
        return false;
    }

    public static bool? GetBoolOrNull(this JsonObject? j, string key) {
        try {
            if (j?[key] is not JsonValue v) return null;
            return v.GetValueKind() switch {
                JsonValueKind.String => string2bool(v.GetValue<string>()),
                JsonValueKind.Number => v.GetValue<long>()!=0,
                JsonValueKind.True or JsonValueKind.False => v.GetValue<bool>(),
                _ => null,
            };
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
        //if (j == null) throw new ArgumentNullException(key);
        //if (j[key] is not JsonValue v) throw new KeyNotFoundException(key);
        //return v.GetValueKind() switch {
        //    JsonValueKind.String => v.GetValue<string>(),
        //    JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => v.ToJsonString(),
        //    _ => throw new InvalidOperationException($"Unexpected value kind {v.GetValueKind()} for key {key}"),
        //};
        var r = GetStringOrNull(j, key);
        if (r == null) {
            throw new KeyNotFoundException(key);
        }
        return r;
    }

    public static string? GetStringOrNull(this JsonObject? j, string key) {
        try {
            if (j?[key] is not JsonValue v) return null;
            return v.GetValueKind() switch {
                JsonValueKind.String => v.GetValue<string>(),
                JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => v.ToJsonString(),
                _ => null,
            };
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
