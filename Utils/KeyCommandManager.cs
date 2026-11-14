using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Reactive.Bindings;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Windows.Input;
using Windows.System;

namespace io.github.toyota32k.BooLib.Utils;

/**
 * コマンドの定義
 * Executable: 処理の内容（デリゲートまたは、ReactiveCommand）
 */
public class UTCommand {
    public static UtLog Logger = KeyCommandManager.Logger;

    public int ID { get; private set; }
    public string Name { get; private set; }
    public string Description { get; private set; } = "";
    public Action? BreakAction { get; private set; } = null;
    public bool Repeatable { get; private set; } = false;

    private object? Executable;

    private UTCommand() {
        Executable = null;
        ID = 0;
        Name = "NOP";
    }

    public UTCommand(int id, string name, Action fn) {
        Executable = fn;
        Name = name;
        ID = id;
    }
    public UTCommand(int id, string name, Action<int> fn) {
        Executable = fn;
        Name = name;
        ID = id;
    }
    public UTCommand(int id, string name, ICommand fn) {
        Executable = fn;
        Name = name;
        ID = id;
    }

    public UTCommand SetBreakAction(Action? breakAction) {
        BreakAction = breakAction;
        return this;
    }
    public UTCommand SetDescription(string description) {
        Description = description;
        return this;
    }
    public UTCommand SetRepeatable(bool repeatable) {
        Repeatable = repeatable;
        return this;
    }

    public void Invoke(int count) {
        if (Executable == null) return;
        var action = Executable as Action;
        if (action != null) {
            action();
            return;
        }
        var action1 = Executable as Action<int>;
        if (action1 != null) {
            action1(count);
        }
        var command = Executable as ICommand;
        if (command != null) {
            command.Execute(count);
            return;
        }
        Logger.Error("invalid executable");
    }

    public static UTCommand NOP = new UTCommand();

    public override string ToString() {
        return string.IsNullOrWhiteSpace(Description) ? Name : Description;
    }

    #region Typical Command Factory

    public static UTCommand SimpleCmd(int id, string name, string desc, Action fn) {
        return new UTCommand(id, name, fn).SetDescription(desc);
    }
    public static UTCommand SimpleCmd(int id, string name, string desc,ICommand fn) {
        return new UTCommand(id, name, fn).SetDescription(desc);
    }
    public static UTCommand RepeatCmd(int id, string name, string desc, Action<int> fn, Action? breakFn=null) {
        return new UTCommand(id, name, fn).SetDescription(desc).SetRepeatable(true).SetBreakAction(breakFn);
    }
    public static UTCommand RepeatCmd(int id, string name, string desc, Action fn, Action? breakFn = null) {
        return new UTCommand(id, name, fn).SetDescription(desc).SetRepeatable(true).SetBreakAction(breakFn);
    }
    public static UTCommand RepeatCmd(int id, string name, string desc, ICommand fn, Action? breakFn = null) {
        return new UTCommand(id, name, fn).SetDescription(desc).SetRepeatable(true).SetBreakAction(breakFn);
    }

    #endregion
}


public class KeyCommandManager {
    public static UtLog Logger = new UtLog("KM");

    /**
     * コマンドヘルプ表示用データ
     */
    public class KeyMapHelpItem {
        public string Key { get; }
        public string Description { get; }
        public int ID { get; }
        public KeyMapHelpItem(VirtualKey key, UTCommand command, string optionKey = "") {
            Key = $"{optionKey}{key}";
            ID = command.ID;
            Description = command.ToString();
        }
    }

    #region Private Fields

    private Dictionary<int, UTCommand> CommandMap = new Dictionary<int, UTCommand>();
    private Dictionary<VirtualKey, UTCommand> SingleKeyCommands = new Dictionary<VirtualKey, UTCommand>();
    private Dictionary<VirtualKey, UTCommand> ControlKeyCommands = new Dictionary<VirtualKey, UTCommand>();
    private Dictionary<VirtualKey, UTCommand> ShiftKeyCommands = new Dictionary<VirtualKey, UTCommand>();
    private Dictionary<VirtualKey, UTCommand> ControlShiftKeyCommands = new Dictionary<VirtualKey, UTCommand>();

    private ReactiveProperty<VirtualKey> ActiveKey { get; } = new ReactiveProperty<VirtualKey>(VirtualKey.None, ReactivePropertyMode.None);
    private ReactiveProperty<bool> Ctrl { get; } = new ReactiveProperty<bool>(false, ReactivePropertyMode.None);
    private ReactiveProperty<bool> Shift { get; } = new ReactiveProperty<bool>(false, ReactivePropertyMode.None);
    private int RepeatCount = 0;

    private ReadOnlyReactiveProperty<UTCommand> CommandFlow { get; }
    private UTCommand CurrentCommand = UTCommand.NOP;
    private DisposableCollection mDisposer = new DisposableCollection();

    #endregion

    #region Construction

    public KeyCommandManager() {
        CommandFlow = ActiveKey.CombineLatest(Ctrl, Shift, (k, c, s) => {
            Debug.WriteLine($"key changed:{k} ctrl={c} shift={s}");
            if (c && s) {
                return ControlShiftKeyCommands.GetValue(k, UTCommand.NOP);
            }
            else if (c) {
                return ControlKeyCommands.GetValue(k, UTCommand.NOP);
            }
            else if (s) {
                return ShiftKeyCommands.GetValue(k, UTCommand.NOP);
            }
            else {
                return SingleKeyCommands.GetValue(k, UTCommand.NOP);
            }
        }).ToReadOnlyReactiveProperty(UTCommand.NOP, ReactivePropertyMode.None);
    }

    #endregion

    #region Command Registration / Command Accessors

    /**
     * コマンドの登録
     */
    public void RegisterCommand(params UTCommand[] commands) {
        foreach (var cmd in commands) {
            CommandMap[cmd.ID] = cmd;
        }
    }

    public UTCommand? CommandOf(string name) {
        return CommandMap.Values.Where(c => c.Name == name).FirstOrDefault();
    }
    public UTCommand? CommandOf(int id) {
        return CommandMap.GetValue(id);
    }
    public UTCommand? this[int id] {
        get => CommandOf(id);
    }
    public UTCommand? this[string name] {
        get => CommandOf(name);
    }
    #endregion

    #region Command / VirtualKey Assignment

    public void AssignSingleKeyCommand(int id, VirtualKey key) {
        var cmd = this[id] ?? throw new ArgumentException($"invalid command id: {id}");
        SingleKeyCommands.Add(key, cmd);
    }
    public void AssignControlKeyCommand(int id, VirtualKey key) {
        var cmd = this[id] ?? throw new ArgumentException($"invalid command id: {id}");
        ControlKeyCommands.Add(key, cmd);
    }
    public void AssignShiftKeyCommand(int id, VirtualKey key) {
        var cmd = this[id] ?? throw new ArgumentException($"invalid command id: {id}");
        ShiftKeyCommands.Add(key, cmd);
    }
    public void AssignControlShiftKeyCommand(int id, VirtualKey key) {
        var cmd = this[id] ?? throw new ArgumentException($"invalid command id: {id}");
        ControlShiftKeyCommands.Add(key, cmd);
    }

    #endregion


    #region Public Operation

    public bool Enabled { get; private set; } = false;

    /**
     * キーイベントを受け取るUIElementを指定して、コマンドマネージャを有効化する。
     * すでに有効化されている場合は何もしない。
     */
    public void Enable(UIElement owner) {
        if (!Enabled) {
            Logger.Info($"Enabling KeyCommandManager");
            Cancel();
            mDisposer.Add(
                GenericDisposable.Create(
                    prepareAction: () => {
                        owner.PreviewKeyDown += OnKeyDown;
                        owner.PreviewKeyUp += OnKeyUp;
                    },
                    disposeAction: () => {
                        owner.PreviewKeyDown -= OnKeyDown;
                        owner.PreviewKeyUp -= OnKeyUp;
                    }),
                CommandFlow.Subscribe(Execute)
            );
            Enabled = true;
        }
    }

    /**
     * キーコマンドマネージャを停止・無効化する。
     * すでに無効化されている場合は何もしない。
     */
    public void Disable() {
        if (Enabled) {
            Logger.Info($"Disabling KeyCommandManager");
            mDisposer.Dispose();
            Enabled = false;
            Cancel();
        }
    }

    public void Cancel() {
        ActiveKey.Value = VirtualKey.None;
        Ctrl.Value = false;
        Shift.Value = false;
        CurrentCommand = UTCommand.NOP;
    }

    public IEnumerable<KeyMapHelpItem> MakeHelpMessage() {
        return SingleKeyCommands.Select(p => new KeyMapHelpItem(p.Key, p.Value))
                .Concat(ControlKeyCommands.Select(p => new KeyMapHelpItem(p.Key, p.Value, "Ctrl+")))
                .Concat(ShiftKeyCommands.Select(p => new KeyMapHelpItem(p.Key, p.Value, "Shift+")))
                .Concat(ControlShiftKeyCommands.Select(p => new KeyMapHelpItem(p.Key, p.Value, "Ctrl+Shift+")))
                .OrderBy(t => t.ID);
    }

    public void Dispose() {
        Disable();
    }

    #endregion

    #region Key Event Handlers  (private)

    private void Execute(UTCommand nextCommand) {
        if (CurrentCommand.ID != nextCommand.ID) {
            // 新規コマンド
            RepeatCount = 0;
            CurrentCommand?.BreakAction?.Invoke();
            CurrentCommand = nextCommand;
        }
        else if (!CurrentCommand.Repeatable) {
            // 繰り返しコマンド
            return;
        }
        //LoggerEx.debug($"{CurrentCommand.Name}");
        CurrentCommand.Invoke(RepeatCount++);
    }


    private void OnKeyDown(object sender, KeyRoutedEventArgs e) {
        //LoggerEx.debug($"Key={e.Key}, Sys={e.SystemKey}, State={e.KeyStates}, Rep={e.IsRepeat}, Down={e.IsDown}, Up={e.IsUp}, Toggled={e.IsToggled}");
        Down(e.Key);
    }
    private void OnKeyUp(object sender, KeyRoutedEventArgs e) {
        //LoggerEx.debug($"Key={e.Key}, Sys={e.SystemKey}, State={e.KeyStates}, Rep={e.IsRepeat}, Down={e.IsDown}, Up={e.IsUp}, Toggled={e.IsToggled}");
        Up(e.Key);
    }

    private void Down(VirtualKey key) {
        Debug.WriteLine($"KeyDown: {key}");
        if (key == VirtualKey.LeftControl || key == VirtualKey.RightControl) {
            Ctrl.Value = true;
            //LoggerEx.debug($"Ctrl=true");
        }
        else if (key == VirtualKey.LeftShift || key == VirtualKey.RightShift) {
            Shift.Value = true;
            //LoggerEx.debug($"Shift=true");
        }
        else {
            //LoggerEx.debug($"{ActiveKey.Value} --> {key}");
            ActiveKey.Value = key;
        }
        //LoggerEx.debug($"Ctrl={Ctrl.Value}, Shift={Shift.Value}, {ActiveKey.Value}");
    }

    private void Up(VirtualKey key) {
        if (key == VirtualKey.LeftControl || key == VirtualKey.RightControl) {
            Ctrl.Value = false;
            //LoggerEx.debug($"Ctrl=false");
        }
        if (key == VirtualKey.LeftShift || key == VirtualKey.RightShift) {
            Shift.Value = false;
            //LoggerEx.debug($"Shift=false");
        }
        else if (key == ActiveKey.Value) {
            ActiveKey.Value = VirtualKey.None;
            //LoggerEx.debug($"{key} --> None");
        }
        //LoggerEx.debug($"Ctrl={Ctrl.Value}, Shift={Shift.Value}, {ActiveKey.Value}");
    }

    #endregion
}
