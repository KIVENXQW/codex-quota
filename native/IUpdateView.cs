using System;

namespace CodexQuota {
  public interface IUpdateView {
    event EventHandler InstallRequested;
    event EventHandler CancelRequested;
    bool IsDisposed { get; }
    void ShowChecking();
    void ShowRelease(UpdateRelease release);
    void ShowInfo(string headline,string detail,bool error);
    void ShowProgress(string detail,int percent);
    void Open();
  }
  public interface IUpdateStatusView : IUpdateView {
    void SetAvailable(UpdateRelease release);
    void SetHandoff();
  }
}
