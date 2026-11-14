using System;
using System.Collections.Generic;
using System.Text;

namespace io.github.toyota32k.BooLib.Utils;

public class GenericDisposable : IDisposable {
    private readonly Action _disposeAction;

    public GenericDisposable(Action disposeAction) {
        _disposeAction = disposeAction;
    }

    public void Dispose() {
        _disposeAction();
    }

    public static GenericDisposable Create(Action prepareAction, Action disposeAction) {
        prepareAction();
        return new GenericDisposable(disposeAction);
    }
    public static GenericDisposable Create(Action disposeAction) {
        return new GenericDisposable(disposeAction);
    }
}
