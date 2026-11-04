using System;
using System.Drawing;
using System.Windows.Forms;

namespace CodexQuota {
  internal sealed class ChartDetailPresenter : IDisposable {
    readonly Control source;
    readonly UsageDetailPopup popup;
    ScrollableControl scrollParent;
    Form owner;
    bool disposed;
    internal bool Visible {get{return !disposed&&popup.Visible;}}
    internal ChartDetailPresenter(Control source,float scale) {
      this.source=source;popup=new UsageDetailPopup(scale);
      source.ParentChanged+=ParentChanged;source.VisibleChanged+=HideForChange;
      source.LocationChanged+=HideForChange;source.SizeChanged+=HideForChange;
      source.HandleDestroyed+=HideForChange;source.Leave+=HideForChange;
      BindParent();
    }
    void ParentChanged(object sender,EventArgs e){Hide();UnbindParent();BindParent();}
    void BindParent() {
      for(Control p=source.Parent;p!=null;p=p.Parent)if(p is ScrollableControl){scrollParent=(ScrollableControl)p;break;}
      if(scrollParent!=null)scrollParent.Scroll+=Scrolled;
      owner=source.FindForm();
      if(owner!=null){owner.Deactivate+=HideForChange;owner.LocationChanged+=HideForChange;owner.SizeChanged+=HideForChange;owner.FormClosed+=OwnerClosed;}
    }
    void UnbindParent() {
      if(scrollParent!=null)scrollParent.Scroll-=Scrolled;scrollParent=null;
      if(owner!=null){owner.Deactivate-=HideForChange;owner.LocationChanged-=HideForChange;owner.SizeChanged-=HideForChange;owner.FormClosed-=OwnerClosed;}owner=null;
    }
    void Scrolled(object sender,ScrollEventArgs e){Hide();}
    void OwnerClosed(object sender,FormClosedEventArgs e){Hide();}
    void HideForChange(object sender,EventArgs e){Hide();}
    internal void Show(ChartBucket bucket,Rectangle anchor,Color accent,bool english) {
      if(disposed)return;
      popup.SetData(bucket,accent,english);
      var form=source.FindForm();
      if(source.IsHandleCreated&&source.Visible&&form!=null&&form.Visible)popup.ShowFor(source,anchor);
    }
    internal void Hide(){if(!disposed&&!popup.IsDisposed)popup.Hide();}
    public void Dispose() {
      if(disposed)return;Hide();disposed=true;UnbindParent();
      source.ParentChanged-=ParentChanged;source.VisibleChanged-=HideForChange;
      source.LocationChanged-=HideForChange;source.SizeChanged-=HideForChange;
      source.HandleDestroyed-=HideForChange;source.Leave-=HideForChange;popup.Dispose();
    }
  }
}
