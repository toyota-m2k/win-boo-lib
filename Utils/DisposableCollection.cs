using System;
using System.Collections.Generic;
using System.Text;

namespace io.github.toyota32k.BooLib.Utils;

public class DisposableCollection : IDisposable {
    private readonly List<IDisposable> _disposables = new List<IDisposable>();
    public void Add(params IDisposable[] disposables) {
        _disposables.AddRange(disposables);
    }
    public void DisposeOne(IDisposable d) {
        _disposables.Remove(d);
        d.Dispose();
    }

    public void Dispose() {
        foreach (var disposable in _disposables) {
            disposable.Dispose();
        }
        _disposables.Clear();
    }
}
