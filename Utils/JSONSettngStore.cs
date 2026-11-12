using io.github.toyota32k.BooLib.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace BooWin.DI.implement.settings;

public class JSONSettngStore {
    private readonly string _filePath;
    private readonly JsonSerializerOptions _options;
    private readonly object _gate = new();
    private JsonObject _root = new();
    private bool _isDirty = false;

    public static JsonSerializerOptions DefaultOptions { get; } = new() {
        WriteIndented = true,
        // 日本語を \uXXXX にエスケープせずに出力する（手で編集しやすくするため）
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    public JSONSettngStore(string filePath, JsonSerializerOptions? options = null) {
        _filePath = Path.GetFullPath(filePath);
        _options = options ?? DefaultOptions;
        Load();
    }

    public void ResetAll() {
        lock (_gate) {
            _root = new JsonObject();
            _isDirty = true;
        }
    }
    // ---------- 値の読み書き ----------

    public T Get<T>(string key, T defaultValue)
        => TryGet(key, out T value) ? value : defaultValue;
    public T? GetOrNull<T>(string key) where T : class
        => TryGet(key, out T? value) ? value : null;

    public bool TryGet<T>(string key, out T value) {
        lock (_gate) {
            if (_root.TryGetPropertyValue(key, out var node) && node is not null) {
                try {
                    value = node.Deserialize<T>(_options)!;
                    return true;
                }
                catch (JsonException) { /* 型が合わないときは既定値を返す */ }
                catch (InvalidOperationException) { }
            }
            value = default!;
            return false;
        }
    }

    public void Set<T>(string key, T value) {
        // 呼び出し側が後からオブジェクトを書き換えても影響しないよう、この時点で JsonNode に変換する
        var node = JsonSerializer.SerializeToNode(value, _options);
        lock (_gate) { 
            _root[key] = node;
            _isDirty = true;
        }
    }


    public bool Contains(string key) { lock (_gate) return _root.ContainsKey(key); }

    public bool Remove(string key) { 
        lock (_gate) { 
            var result = _root.Remove(key);
            if (result) _isDirty = true;
            return result;
        }
    }


    public T Get<T>(SettingKey<T> key)
        => TryGet(key.Name, out T value) ? value : key.CreateDefault();

    public void Set<T>(SettingKey<T> key, T value) => Set(key.Name, value);


    // ---------- 永続化 ----------

    private void Load() {
        lock (_gate) {
            _root = TryRead(_filePath)
                 ?? TryRead(_filePath + ".bak")   // 本体が壊れていたらバックアップを読む
                 ?? new JsonObject();
            _isDirty = false;
        }
    }

    public void Save(bool force = false) {
        if (!force && !_isDirty) return;
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        var tmp = _filePath + ".tmp";

        lock (_gate) {
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new Utf8JsonWriter(fs, new JsonWriterOptions { Indented = true, Encoder = _options.Encoder })) {
                _root.WriteTo(writer, _options);
            }
        }

        // 一時ファイルに書いてから置き換える（書き込み中に落ちてもファイルが壊れない）
        if (File.Exists(_filePath)) { 
            File.Replace(tmp, _filePath, _filePath + ".bak");
        } else {
            File.Move(tmp, _filePath);
        }
        _isDirty = false;
    }

    private JsonObject? TryRead(string path) {
        if (!File.Exists(path)) return null;
        try {
            using var fs = File.OpenRead(path);
            return JsonNode.Parse(fs) as JsonObject;
        }
        catch (JsonException) {
            return null;
        }
    }
}


public sealed class SettingKey<T> {
    private readonly Func<T> _defaultFactory;
    public string Name { get; }

    // int や string など、共有しても問題ない既定値向け
    public SettingKey(string name, T defaultValue) : this(name, () => defaultValue) { }

    // List などの可変オブジェクト向け（呼ぶたびに新しいインスタンスを返す）
    public SettingKey(string name, Func<T> defaultFactory) {
        Name = name;
        _defaultFactory = defaultFactory;
    }

    public T CreateDefault() => _defaultFactory();
}