using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

static class QuotaText {
 public static string ColorFor(double remaining){return remaining<=10?"#FF3B30":remaining<=30?"#D9A000":"#34C759";}
 public static string Message(double remaining) {
  if(remaining<=0)return "额度已用完，等重置吧";
  if(remaining<=10)return "快到上限，稍作休息";
  if(remaining<=30)return "留点余量，先做重点";
  if(remaining<=60)return "节奏不错，稳步推进";
  return "余量充足，放心推进";
 }
 public static string Reset(long epoch,DateTimeOffset now) {
  if(epoch<=0)return "重置时间未知";
  var reset=DateTimeOffset.FromUnixTimeSeconds(epoch);var span=reset-now;
  if(span.TotalSeconds<=0)return "等待额度刷新";
  if(span.TotalDays>=1)return (int)span.TotalDays+" 天 "+span.Hours+" 小时后重置";
  if(span.TotalHours>=1)return (int)span.TotalHours+" 小时 "+span.Minutes+" 分钟后重置";
  return Math.Ceiling(span.TotalMinutes)+" 分钟后重置";
 }
}

static class Native {
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr h,int id,uint modifiers,uint key);
 [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h,int id);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
 [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindow callback,IntPtr p);
 delegate bool EnumWindow(IntPtr hwnd,IntPtr param);
 [DllImport("user32.dll")] static extern int GetWindowText(IntPtr h,System.Text.StringBuilder text,int length);
 [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr context);
 public static uint Pid(IntPtr h){uint id;GetWindowThreadProcessId(h,out id);return id;}
 public static bool IsCodexProcess(string name,string path) {
  if(name.Equals("Codex",StringComparison.OrdinalIgnoreCase))return true;
  return name.Equals("ChatGPT",StringComparison.OrdinalIgnoreCase)&&path!=null&&(path.IndexOf("OpenAI.Codex_",StringComparison.OrdinalIgnoreCase)>=0||path.IndexOf("\\OpenAI\\Codex\\",StringComparison.OrdinalIgnoreCase)>=0);
 }
 public static bool IsCodex(IntPtr h) {
  if(h==IntPtr.Zero||!IsWindowVisible(h))return false;
  try {using(var p=Process.GetProcessById((int)Pid(h))){string name=p.ProcessName;if(name.Equals("Codex",StringComparison.OrdinalIgnoreCase))return true;if(!name.Equals("ChatGPT",StringComparison.OrdinalIgnoreCase))return false;return IsCodexProcess(name,p.MainModule.FileName);}}catch{return false;}
 }
 public static string Diagnostic() {
  var list=new List<string>();EnumWindows((h,p)=>{if(IsCodex(h)){var title=new System.Text.StringBuilder(256);GetWindowText(h,title,title.Capacity);list.Add("Codex window: pid="+Pid(h)+" title="+title);}return true;},IntPtr.Zero);
  list.Add("Foreground is Codex: "+IsCodex(GetForegroundWindow()));return String.Join(Environment.NewLine,list);
 }
}

class Preferences {
 public double X=-1,Y=-1;public bool Compact=false,ForegroundOnly=true;public int Tint=86;public double Transparency=14;
 public static string FilePath {get{return System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"settings.json");}}
 public static Preferences Load(){try{return new JavaScriptSerializer().Deserialize<Preferences>(File.ReadAllText(FilePath))??new Preferences();}catch{return new Preferences();}}
 public void Save(){try{File.WriteAllText(FilePath,new JavaScriptSerializer().Serialize(this));}catch{}}
}

class QuotaWindow:Window {
 readonly Preferences preferences;readonly bool preview;
 readonly DispatcherTimer foreground=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(150)};
 readonly DispatcherTimer refresh=new DispatcherTimer{Interval=TimeSpan.FromSeconds(60)};
 readonly DispatcherTimer countdown=new DispatcherTimer{Interval=TimeSpan.FromSeconds(15)};
 readonly CancellationTokenSource shutdown=new CancellationTokenSource();
 readonly List<Tuple<WindowQuota,TextBlock>> countdownLabels=new List<Tuple<WindowQuota,TextBlock>>();
 List<WindowQuota> quotas=new List<WindowQuota>();Border shell,cardSurface,shadowSurface;StackPanel body;TextBlock footerStatus,footerMessage;Grid root;
 DateTime wakeUntil;bool hotkeyRegistered;public EventWaitHandle WakeSignal;
 IntPtr hwnd,lastCodex;DateTime lastUpdate;bool busy,closed,glassAvailable;double cornerRadius=28;string error;System.Windows.Forms.NotifyIcon tray;Popup settingsPopup;Button toggleButton,expandedToggle,compactToggle;StackPanel expandedView,compactView;double expandedCardHeight;RotateTransform refreshRotation=new RotateTransform(0);int motionVersion;
 public QuotaWindow(bool isPreview,bool compactPreview) {
  preview=isPreview;preferences=preview?new Preferences{Compact=compactPreview}:Preferences.Load();
  Title="Codex Quota Glass";Width=304;SizeToContent=SizeToContent.Height;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;ShowInTaskbar=isPreview;ShowActivated=false;Topmost=true;
  AllowsTransparency=true;Background=Brushes.Transparent;UseLayoutRounding=true;SnapsToDevicePixels=true;
  TextOptions.SetTextFormattingMode(this,TextFormattingMode.Display);TextOptions.SetTextRenderingMode(this,TextRenderingMode.Grayscale);
  FontFamily=new FontFamily("Segoe UI, Microsoft YaHei UI");

  var area=SystemParameters.WorkArea;Left=preferences.X>=area.Left&&preferences.X<area.Right-100?preferences.X:area.Right-Width-24;Top=preferences.Y>=area.Top&&preferences.Y<area.Bottom-70?preferences.Y:area.Top+70;
  SourceInitialized+=(s,e)=>{hwnd=new WindowInteropHelper(this).Handle;glassAvailable=true;Build();if(!preview){hotkeyRegistered=Native.RegisterHotKey(hwnd,1,0x4003,0x51);HwndSource.FromHwnd(hwnd).AddHook((IntPtr h,int msg,IntPtr w,IntPtr l,ref bool handled)=>{if(msg==0x0312&&w.ToInt32()==1){Wake();handled=true;}return IntPtr.Zero;});}};
  MouseLeftButtonDown+=(s,e)=>{if(e.Handled)return;if(e.ClickCount==2){ToggleCompact();return;}try{DragMove();preferences.X=Left;preferences.Y=Top;if(!preview)preferences.Save();}catch{} };
  LocationChanged+=(s,e)=>{if(!preview&&!closed){preferences.X=Left;preferences.Y=Top;if(!preview)preferences.Save();}};
  Closed+=(s,e)=>{closed=true;if(hotkeyRegistered)Native.UnregisterHotKey(hwnd,1);if(settingsPopup!=null)settingsPopup.IsOpen=false;shutdown.Cancel();foreground.Stop();refresh.Stop();countdown.Stop();if(tray!=null)tray.Dispose();shutdown.Dispose();Application.Current.Shutdown();};
  foreground.Tick+=(s,e)=>{if(WakeSignal!=null&&WakeSignal.WaitOne(0))Wake();UpdateVisibility();};refresh.Tick+=(s,e)=>Refresh();countdown.Tick+=(s,e)=>UpdateCountdown();
  if(preview){lastUpdate=DateTime.Now;quotas=new List<WindowQuota>{new WindowQuota{Name="5 小时",Left=82,Reset=DateTimeOffset.UtcNow.ToUnixTimeSeconds()+7140},new WindowQuota{Name="每周",Left=64,Reset=DateTimeOffset.UtcNow.ToUnixTimeSeconds()+176400}};}
  else {
   tray=new System.Windows.Forms.NotifyIcon{Icon=System.Drawing.SystemIcons.Information,Text="Codex 额度悬浮窗",Visible=true};
   var menu=new System.Windows.Forms.ContextMenuStrip();
   tray.DoubleClick+=(s,e)=>Dispatcher.Invoke(new Action(Wake));
   menu.Items.Add("显示悬浮窗   Ctrl+Alt+Q",null,(s,e)=>Dispatcher.Invoke(new Action(Wake)));
   menu.Items.Add("立即刷新",null,(s,e)=>Dispatcher.Invoke(new Action(Refresh)));
   var only=new System.Windows.Forms.ToolStripMenuItem("仅 Codex 前台显示"){Checked=preferences.ForegroundOnly,CheckOnClick=true};only.CheckedChanged+=(s,e)=>Dispatcher.Invoke(new Action(()=>{preferences.ForegroundOnly=only.Checked;if(!preview)preferences.Save();UpdateVisibility();}));menu.Items.Add(only);
   menu.Items.Add("收起 / 展开",null,(s,e)=>Dispatcher.Invoke(new Action(ToggleCompact)));
   menu.Items.Add("退出",null,(s,e)=>Dispatcher.Invoke(new Action(Close)));tray.ContextMenuStrip=menu;
   foreground.Start();refresh.Start();countdown.Start();Refresh();
  }
  Build();
 }
 Brush Ink(string hex){return (Brush)new BrushConverter().ConvertFromString(hex);}
 TextBlock Text(string value,double size,string color="#26313B",FontWeight? weight=null){return new TextBlock{Text=value,FontSize=size,Foreground=Ink(color),FontWeight=weight??FontWeights.Normal,VerticalAlignment=VerticalAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis};}
 TextBlock Percent(WindowQuota quota,double size){var text=Text(quota.Left.ToString("0.#",CultureInfo.InvariantCulture)+"%",size,QuotaText.ColorFor(quota.Left),FontWeights.Medium);text.TextTrimming=TextTrimming.None;text.TextWrapping=TextWrapping.NoWrap;text.Measure(new Size(Double.PositiveInfinity,Double.PositiveInfinity));text.Width=Math.Ceiling(text.DesiredSize.Width)+2;return text;}
 Button IconButton(string tooltip,string geometry,Action click) {
  // A fixed 20px drawing viewport keeps strokes and icon proportions consistent.
  var drawing=new System.Windows.Shapes.Path{Data=Geometry.Parse(geometry),Stroke=Ink("#768391"),StrokeThickness=1.5,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round};
  var viewport=new Canvas{Width=20,Height=20};viewport.Children.Add(drawing);
  var tip=new ToolTip{Content=tooltip,Background=Ink("#F8FAFC"),Foreground=Ink("#66737F"),BorderThickness=new Thickness(0),Padding=new Thickness(9,5,9,5),FontSize=10};
  var edge=new FrameworkElementFactory(typeof(Border));edge.SetValue(Border.CornerRadiusProperty,new CornerRadius(8));edge.SetValue(Border.BackgroundProperty,Ink("#F8FAFC"));edge.SetValue(Border.PaddingProperty,new Thickness(9,5,9,5));edge.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));tip.Template=new ControlTemplate(typeof(ToolTip)){VisualTree=edge};
  var button=new Button{Content=viewport,ToolTip=tip,Width=28,Height=28,Background=Brushes.Transparent,BorderThickness=new Thickness(0),Cursor=Cursors.Hand,Focusable=true};
  System.Windows.Automation.AutomationProperties.SetName(button,tooltip);ToolTipService.SetInitialShowDelay(button,650);
  if(tooltip=="刷新额度"){viewport.RenderTransformOrigin=new Point(.5,.5);viewport.RenderTransform=refreshRotation;}
  RoundedButton(button);
  button.Click+=(s,e)=>{click();e.Handled=true;};return button;
 }
 static void Animate(UIElement target,DependencyProperty property,double value,int milliseconds){var animation=new DoubleAnimation(value,TimeSpan.FromMilliseconds(SystemParameters.ClientAreaAnimation?milliseconds:0)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut}};target.BeginAnimation(property,animation);}
 void RoundedButton(Button button) {
  var frame=new FrameworkElementFactory(typeof(Border));frame.Name="Surface";frame.SetValue(Border.CornerRadiusProperty,new CornerRadius(10));frame.SetValue(Border.BackgroundProperty,new SolidColorBrush(Color.FromArgb(0,255,255,255)));
  var presenter=new FrameworkElementFactory(typeof(ContentPresenter));presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty,HorizontalAlignment.Center);presenter.SetValue(ContentPresenter.VerticalAlignmentProperty,VerticalAlignment.Center);frame.AppendChild(presenter);
  button.Template=new ControlTemplate(typeof(Button)){VisualTree=frame};button.RenderTransformOrigin=new Point(.5,.5);var scale=new ScaleTransform(1,1);button.RenderTransform=scale;
  Action<double,int> zoom=(v,ms)=>{var a=new DoubleAnimation(v,TimeSpan.FromMilliseconds(SystemParameters.ClientAreaAnimation?ms:0)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut}};scale.BeginAnimation(ScaleTransform.ScaleXProperty,a);scale.BeginAnimation(ScaleTransform.ScaleYProperty,a);};
  Action<byte> tint=a=>{button.ApplyTemplate();var border=button.Template.FindName("Surface",button) as Border;if(border!=null){var brush=border.Background as SolidColorBrush;if(brush!=null&&brush.IsFrozen){brush=brush.Clone();border.Background=brush;}if(brush!=null)brush.BeginAnimation(SolidColorBrush.ColorProperty,new ColorAnimation(Color.FromArgb(a,255,255,255),TimeSpan.FromMilliseconds(SystemParameters.ClientAreaAnimation?120:0)));}};
  button.MouseEnter+=(s,e)=>tint(170);button.MouseLeave+=(s,e)=>{tint(0);zoom(1,180);};button.PreviewMouseLeftButtonDown+=(s,e)=>{tint(230);zoom(.96,80);};button.PreviewMouseLeftButtonUp+=(s,e)=>zoom(1,180);
 }
 void ToggleCompact(){
  if(settingsPopup!=null)settingsPopup.IsOpen=false;
  preferences.Compact=!preferences.Compact;if(!preview)preferences.Save();
  toggleButton=preferences.Compact?compactToggle:expandedToggle;
  var incoming=preferences.Compact?compactView:expandedView;var outgoing=preferences.Compact?expandedView:compactView;
  incoming.IsHitTestVisible=true;outgoing.IsHitTestVisible=false;
  double target=preferences.Compact?67:expandedCardHeight;
  int ms=SystemParameters.ClientAreaAnimation?240:0;
  var ease=new CubicEase{EasingMode=EasingMode.EaseInOut};
  // Both layouts are retained. No rebuild, snapshot, HWND resize or scale reset.
  cardSurface.BeginAnimation(HeightProperty,new DoubleAnimation(target,TimeSpan.FromMilliseconds(ms)){EasingFunction=ease});
  shadowSurface.RenderTransform=Transform.Identity;
  shadowSurface.BeginAnimation(HeightProperty,new DoubleAnimation(target,TimeSpan.FromMilliseconds(ms)){EasingFunction=ease});
  var clip=(RectangleGeometry)root.Clip;
  clip.BeginAnimation(RectangleGeometry.RectProperty,new RectAnimation(new Rect(0,0,244,Math.Max(0,target-26)),TimeSpan.FromMilliseconds(ms)){EasingFunction=ease});
  Animate(outgoing,OpacityProperty,0,100);Animate(incoming,OpacityProperty,1,180);
 }
 void StopRefreshSpin(){double angle=refreshRotation.Angle;refreshRotation.BeginAnimation(RotateTransform.AngleProperty,null);refreshRotation.Angle=angle;double finish=(Math.Floor(angle/360)+1)*360;var settle=new DoubleAnimation(angle,finish,TimeSpan.FromMilliseconds(Math.Max(160,(finish-angle)/360*900))){EasingFunction=new QuadraticEase{EasingMode=EasingMode.EaseOut}};settle.Completed+=(s,e)=>{refreshRotation.BeginAnimation(RotateTransform.AngleProperty,null);refreshRotation.Angle=0;};refreshRotation.BeginAnimation(RotateTransform.AngleProperty,settle);}
 void Menu(Button anchor) {
  if(settingsPopup!=null&&settingsPopup.IsOpen){CloseSettings();return;}
  var content=new StackPanel();var title=new Grid();title.ColumnDefinitions.Add(new ColumnDefinition());title.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});title.Children.Add(Text("透明度",13,"#26313B",FontWeights.SemiBold));var close=IconButton("关闭设置","M 3,3 L 11,11 M 11,3 L 3,11",CloseSettings);Grid.SetColumn(close,1);title.Children.Add(close);content.Children.Add(title);
  var value=Text(preferences.Transparency.ToString("0")+"%",11,"#75818C");value.Margin=new Thickness(0,6,0,10);content.Children.Add(value);
  var slider=new Slider{Minimum=5,Maximum=65,Value=Math.Max(5,Math.Min(65,preferences.Transparency)),SmallChange=1,LargeChange=5,TickFrequency=1,IsSnapToTickEnabled=true,Height=24,ToolTip="仅改变玻璃背景透明度"};
  slider.Template=(ControlTemplate)System.Windows.Markup.XamlReader.Parse(@"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='{x:Type Slider}' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'><Grid Height='24'><Border Height='4' CornerRadius='2' Background='#DAE0E6'/><Track x:Name='PART_Track' Minimum='{TemplateBinding Minimum}' Maximum='{TemplateBinding Maximum}' Value='{TemplateBinding Value}'><Track.DecreaseRepeatButton><RepeatButton Command='Slider.DecreaseLarge' Focusable='False'><RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Border Height='4' CornerRadius='2' Background='#8294AA'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton><Track.Thumb><Thumb Width='20' Height='20' Cursor='Hand'><Thumb.Template><ControlTemplate TargetType='Thumb'><Border CornerRadius='10' Background='White' BorderBrush='#D9DFE5' BorderThickness='.7'/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb><Track.IncreaseRepeatButton><RepeatButton Command='Slider.IncreaseLarge' Focusable='False'><RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton></Track></Grid></ControlTemplate>");
  slider.ValueChanged+=(s,e)=>{preferences.Transparency=e.NewValue;value.Text=e.NewValue.ToString("0")+"%";UpdateGlass();};slider.AddHandler(Thumb.DragCompletedEvent,new DragCompletedEventHandler((s,e)=>{if(!preview)preferences.Save();}));slider.LostKeyboardFocus+=(s,e)=>{if(!preview)preferences.Save();};content.Children.Add(slider);
  var onlyRow=new Grid{Margin=new Thickness(0,16,0,0)};onlyRow.ColumnDefinitions.Add(new ColumnDefinition());onlyRow.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});onlyRow.Children.Add(Text("仅 Codex 前台显示",11,"#66737F"));
  var only=new ToggleButton{IsChecked=preferences.ForegroundOnly,Width=34,Height=20,Cursor=Cursors.Hand};System.Windows.Automation.AutomationProperties.SetName(only,"仅 Codex 前台显示");
  only.Template=(ControlTemplate)System.Windows.Markup.XamlReader.Parse(@"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='ToggleButton'><Grid><Border x:Name='Track' Background='#DCE2E8' CornerRadius='10'/><Border Width='16' Height='16' Margin='2' HorizontalAlignment='Left' Background='White' CornerRadius='8'><Border.RenderTransform><TranslateTransform x:Name='Knob'/></Border.RenderTransform></Border></Grid><ControlTemplate.Triggers><Trigger Property='IsChecked' Value='True'><Setter TargetName='Track' Property='Background' Value='#6AA99E'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");
  only.Loaded+=(s,e)=>{var knob=only.Template.FindName("Knob",only) as TranslateTransform;if(knob!=null)knob.X=only.IsChecked==true?14:0;};only.Click+=(s,e)=>{var knob=only.Template.FindName("Knob",only) as TranslateTransform;if(knob!=null)knob.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(only.IsChecked==true?14:0,TimeSpan.FromMilliseconds(SystemParameters.ClientAreaAnimation?160:0)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut}});preferences.ForegroundOnly=only.IsChecked==true;if(!preview)preferences.Save();UpdateVisibility();};Grid.SetColumn(only,1);onlyRow.Children.Add(only);content.Children.Add(onlyRow);
  var panel=new Border{Width=240,Padding=new Thickness(16,12,16,16),CornerRadius=new CornerRadius(22),Background=new SolidColorBrush(Color.FromArgb(245,248,250,252)),BorderBrush=Brushes.White,BorderThickness=new Thickness(1),Child=content};
  settingsPopup=new Popup{Child=panel,PlacementTarget=anchor,Placement=PlacementMode.Bottom,HorizontalOffset=-208,VerticalOffset=8,AllowsTransparency=true,StaysOpen=false,PopupAnimation=PopupAnimation.None};settingsPopup.Closed+=(s,e)=>{if(!preview)preferences.Save();};settingsPopup.IsOpen=true;
  panel.Opacity=0;var shift=new TranslateTransform(0,-6);panel.RenderTransform=shift;Animate(panel,OpacityProperty,1,180);shift.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(0,TimeSpan.FromMilliseconds(SystemParameters.ClientAreaAnimation?180:0)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut}});
 }
 void CloseSettings(){if(settingsPopup==null||!settingsPopup.IsOpen)return;var popup=settingsPopup;var panel=popup.Child;var a=new DoubleAnimation(0,TimeSpan.FromMilliseconds(SystemParameters.ClientAreaAnimation?140:0));a.Completed+=(s,e)=>popup.IsOpen=false;panel.BeginAnimation(OpacityProperty,a);if(!preview)preferences.Save();}
 void UpdateGlass(){if(cardSurface!=null)cardSurface.Background=glassAvailable?new SolidColorBrush(Color.FromArgb((byte)(255*(1-Math.Max(5,Math.Min(65,preferences.Transparency))/100)),255,255,255)):Brushes.White;}
 void Build() {
  ++motionVersion;if(settingsPopup!=null)settingsPopup.IsOpen=false;countdownLabels.Clear();var area=SystemParameters.WorkArea;Width=304;cornerRadius=preferences.Compact?34:28;
  shell=new Border{CornerRadius=new CornerRadius(cornerRadius),BorderBrush=new SolidColorBrush(Color.FromArgb(210,255,255,255)),BorderThickness=new Thickness(1),Padding=new Thickness(18,12,18,12)};

  root=new Grid{ClipToBounds=true};body=new StackPanel();root.Children.Add(body);shell.Child=root;shell.Margin=new Thickness(12);shell.Background=Brushes.Transparent;shell.BorderBrush=Brushes.Transparent;shell.VerticalAlignment=VerticalAlignment.Top;
  var host=new Grid();cardSurface=new Border{CornerRadius=new CornerRadius(cornerRadius),Margin=new Thickness(12),BorderBrush=new SolidColorBrush(Color.FromArgb(210,255,255,255)),BorderThickness=new Thickness(1),VerticalAlignment=VerticalAlignment.Top,CacheMode=new BitmapCache()};
  shadowSurface=new Border{CornerRadius=new CornerRadius(cornerRadius),Margin=new Thickness(16,18,16,6),Background=new SolidColorBrush(Color.FromArgb(24,28,43,58)),VerticalAlignment=VerticalAlignment.Top,Effect=new System.Windows.Media.Effects.BlurEffect{Radius=12},CacheMode=new BitmapCache(),IsHitTestVisible=false};
  cardSurface.SetBinding(FrameworkElement.HeightProperty,new System.Windows.Data.Binding("ActualHeight"){Source=shell});shadowSurface.SetBinding(FrameworkElement.HeightProperty,new System.Windows.Data.Binding("ActualHeight"){Source=shell});UpdateGlass();host.Children.Add(shadowSurface);host.Children.Add(cardSurface);host.Children.Add(shell);Content=host;

  var header=new Grid{Height=41,Margin=new Thickness(0,0,0,8)};header.ColumnDefinitions.Add(new ColumnDefinition());header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
  header.Children.Add(Text("codex",12,"#26313B",FontWeights.SemiBold));
  var actions=new StackPanel{Orientation=Orientation.Horizontal};Grid.SetColumn(actions,1);header.Children.Add(actions);
  actions.Children.Add(IconButton("刷新额度","M 16,7 A 6.5,6.5 0 0 0 4,7 M 16,3 L 16,7 L 12,7 M 4,13 A 6.5,6.5 0 0 0 16,13 M 4,17 L 4,13 L 8,13",Refresh));
  Button more=null;more=IconButton("设置","M 4,7 L 16,7 M 4,13 L 16,13 M 8,5 L 8,9 M 13,11 L 13,15",()=>Menu(more));actions.Children.Add(more);toggleButton=IconButton("收起","M 6,12 L 10,8 L 14,12",ToggleCompact);actions.Children.Add(toggleButton);body.Children.Add(header);var outer=body;body=new StackPanel();outer.Children.Add(body);
  if(quotas.Count==0)body.Children.Add(Text(busy?"正在查询额度…":error??"等待额度数据",12,"#75818C"));
  for(int i=0;i<quotas.Count;i++) {
   var q=quotas[i];var row=new Grid();row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
   var label=Text(q.Name,12,"#65727E");label.ToolTip=q.Name;row.Children.Add(label);
   var percent=Percent(q,27);Grid.SetColumn(percent,1);row.Children.Add(percent);body.Children.Add(row);
   var reset=Text(QuotaText.Reset(q.Reset,DateTimeOffset.UtcNow),10,"#7A8792");reset.Margin=new Thickness(0,4,0,5);reset.ToolTip=q.Reset>0?DateTimeOffset.FromUnixTimeSeconds(q.Reset).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"):"未知";body.Children.Add(reset);countdownLabels.Add(Tuple.Create(q,reset));
   var track=new Grid{Height=4,Margin=new Thickness(0,0,0,i==quotas.Count-1?10:12)};
   track.Children.Add(new Border{Background=new SolidColorBrush(Color.FromArgb(20,52,70,91)),CornerRadius=new CornerRadius(2.5)});
   var fill=new Border{HorizontalAlignment=HorizontalAlignment.Left,CornerRadius=new CornerRadius(2.5),Background=new LinearGradientBrush(new GradientStopCollection{new GradientStop((Color)ColorConverter.ConvertFromString("#54C8B5"),0),new GradientStop((Color)ColorConverter.ConvertFromString("#65BCE4"),.33),new GradientStop((Color)ColorConverter.ConvertFromString("#8B94EE"),.67),new GradientStop((Color)ColorConverter.ConvertFromString("#B792DF"),1)},new Point(0,0.5),new Point(1,0.5))};
   track.Children.Add(fill);track.SizeChanged+=(s,e)=>fill.Width=Math.Max(0,track.ActualWidth*q.Left/100);body.Children.Add(track);
   if(i<quotas.Count-1)body.Children.Add(new Border{Height=1,Background=new SolidColorBrush(Color.FromArgb(23,83,100,117)),Margin=new Thickness(0,-4,0,8)});
  }
  var footer=new Grid();footer.ColumnDefinitions.Add(new ColumnDefinition());footer.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
  footerStatus=Text("",9,"#85919B");footerStatus.MaxWidth=150;footerMessage=Text("",9,"#778591");Grid.SetColumn(footerMessage,1);footer.Children.Add(footerStatus);footer.Children.Add(footerMessage);body.Children.Add(footer);UpdateFooter();
  if(quotas.Count>6){Height=Math.Min(area.Height-50,700);SizeToContent=SizeToContent.Manual;var scroll=new ScrollViewer{Content=shell,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};Content=scroll;}
  else {PrepareRetainedViews();}
 }
 void PrepareRetainedViews(){
  expandedView=(StackPanel)root.Children[0];expandedToggle=toggleButton;
  expandedView.Measure(new Size(244,Double.PositiveInfinity));expandedCardHeight=expandedView.DesiredSize.Height+26;
  root.Height=expandedView.DesiredSize.Height;root.VerticalAlignment=VerticalAlignment.Top;
  var status=footerStatus;var message=footerMessage;compactView=new StackPanel{VerticalAlignment=VerticalAlignment.Top};body=compactView;BuildCompact();compactToggle=toggleButton;footerStatus=status;footerMessage=message;root.Children.Add(compactView);
  shell.Height=expandedCardHeight;cardSurface.ClearValue(HeightProperty);cardSurface.Height=preferences.Compact?67:expandedCardHeight;
  shadowSurface.ClearValue(HeightProperty);shadowSurface.Height=preferences.Compact?67:expandedCardHeight;shadowSurface.RenderTransform=Transform.Identity;
  root.Clip=new RectangleGeometry(new Rect(0,0,244,(preferences.Compact?67:expandedCardHeight)-26));
  expandedView.Opacity=preferences.Compact?0:1;compactView.Opacity=preferences.Compact?1:0;expandedView.IsHitTestVisible=!preferences.Compact;compactView.IsHitTestVisible=preferences.Compact;
  toggleButton=preferences.Compact?compactToggle:expandedToggle;
  Height=expandedCardHeight+24;SizeToContent=SizeToContent.Manual;
 }
 void BuildCompact() {
  var row=new Grid{Height=41};row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(48)});row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(28)});
  var brand=new StackPanel{VerticalAlignment=VerticalAlignment.Center};brand.Children.Add(Text("codex",12,"#26313B",FontWeights.SemiBold));row.Children.Add(brand);
  var metrics=new UniformGrid{Rows=1,Columns=Math.Max(1,quotas.Count)};Grid.SetColumn(metrics,1);row.Children.Add(metrics);
  foreach(var q in quotas){var metric=new StackPanel{Margin=new Thickness(8,0,0,0)};var label=Text(q.Name,9,"#7A8792");label.ToolTip=q.Name;metric.Children.Add(label);metric.Children.Add(Percent(q,21));metrics.Children.Add(metric);}
  if(quotas.Count==0)metrics.Children.Add(Text("加载中",12,"#7A8792"));
  var expand=IconButton("展开","M 6,8 L 10,12 L 14,8",ToggleCompact);toggleButton=expand;Grid.SetColumn(expand,2);row.Children.Add(expand);body.Children.Add(row);
  footerStatus=Text("",9,"#85919B");footerMessage=Text("",9,"#778591");row.ToolTip=QuotaText.Message(quotas.Count==0?0:quotas.Min(q=>q.Left));
  SizeToContent=SizeToContent.Height;Height=Double.NaN;
 }
 void UpdateFooter() {
  if(footerStatus==null)return;footerStatus.Text=preview?"演示数据":busy?"刷新中…":error!=null?"刷新失败 · 旧数据":"已同步 "+lastUpdate.ToString("HH:mm");footerStatus.ToolTip=error;
  bool expired=quotas.Any(q=>q.Reset>0&&q.Reset<=DateTimeOffset.UtcNow.ToUnixTimeSeconds());
  footerMessage.Text=error!=null?"稍后重试，检查登录/网络":expired?"等待额度刷新":quotas.Count==0?"":QuotaText.Message(quotas.Min(q=>q.Left));
 }
 void UpdateCountdown(){foreach(var pair in countdownLabels)pair.Item2.Text=QuotaText.Reset(pair.Item1.Reset,DateTimeOffset.UtcNow);UpdateFooter();}
 async void Refresh() {
  if(busy||closed||preview)return;busy=true;if(refreshRotation!=null&&SystemParameters.ClientAreaAnimation)refreshRotation.BeginAnimation(RotateTransform.AngleProperty,new DoubleAnimation(0,360,TimeSpan.FromMilliseconds(900)){RepeatBehavior=RepeatBehavior.Forever});UpdateFooter();
  try {var data=await Task.Run(()=>QuotaClient.Read());if(closed)return;quotas=data;lastUpdate=DateTime.Now;error=null;Build();}
  catch(Exception ex){if(!closed){error=ex.Message;Build();}}
  finally {busy=false;if(refreshRotation!=null&&SystemParameters.ClientAreaAnimation)StopRefreshSpin();if(!closed)UpdateFooter();}
 }
 void UpdateVisibility() {
  if(closed||preview)return;IntPtr current=Native.GetForegroundWindow();bool codex=Native.IsCodex(current);
  bool own=current!=IntPtr.Zero&&Native.Pid(current)==(uint)Process.GetCurrentProcess().Id;
  if(codex&&lastCodex!=current){lastCodex=current;if(hwnd!=IntPtr.Zero){}}
  bool show=DateTime.UtcNow<wakeUntil||!preferences.ForegroundOnly||codex||(own&&IsVisible&&lastCodex!=IntPtr.Zero);
  if(show&&!IsVisible)Show();else if(!show&&IsVisible){if(settingsPopup!=null)settingsPopup.IsOpen=false;Hide();}
 }
 public void Wake(){if(closed)return;wakeUntil=DateTime.UtcNow.AddSeconds(15);Show();Activate();if(tray!=null)tray.Text=hotkeyRegistered?"Codex 额度 · Ctrl+Alt+Q 唤醒":"Codex 额度 · 双击托盘唤醒（快捷键被占用）";}
 public void Start(){if(preview){Show();return;}new WindowInteropHelper(this).EnsureHandle();UpdateVisibility();}
 public void ExportPreview(string path){if(Environment.GetEnvironmentVariable("CODEX_QUOTA_PREVIEW_FULL")=="1"){foreach(var q in quotas)q.Left=100;Build();}UpdateLayout();var image=new RenderTargetBitmap((int)(ActualWidth*3),(int)(ActualHeight*3),288,288,PixelFormats.Pbgra32);image.Render(this);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using(var stream=File.Create(path))encoder.Save(stream);}
 public void ExportSettings(string path){var anchor=FindVisual<Button>(this);Menu(anchor);var panel=(FrameworkElement)settingsPopup.Child;panel.BeginAnimation(OpacityProperty,null);panel.Opacity=1;panel.RenderTransform=Transform.Identity;panel.UpdateLayout();var image=new RenderTargetBitmap((int)(panel.ActualWidth*3),(int)(panel.ActualHeight*3),288,288,PixelFormats.Pbgra32);image.Render(panel);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using(var stream=File.Create(path))encoder.Save(stream);settingsPopup.IsOpen=false;}
 static T FindVisual<T>(DependencyObject parent) where T:DependencyObject {for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var child=VisualTreeHelper.GetChild(parent,i);if(child is T)return (T)child;var found=FindVisual<T>(child);if(found!=null)return found;}return null;}
 static void CheckHover(DependencyObject parent){var button=parent as Button;if(button!=null){button.ApplyTemplate();button.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0){RoutedEvent=Mouse.MouseEnterEvent});button.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0){RoutedEvent=Mouse.MouseLeaveEvent});}for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)CheckHover(VisualTreeHelper.GetChild(parent,i));}
 static void CheckPercentBounds(DependencyObject parent){var text=parent as TextBlock;if(text!=null&&text.Text=="100%"){var point=text.TranslatePoint(new Point(0,0),(UIElement)text.Parent);var container=(FrameworkElement)text.Parent;if(text.TextTrimming!=TextTrimming.None||text.ActualWidth+point.X>container.ActualWidth+.5)throw new Exception("100% quota clipped");}for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)CheckPercentBounds(VisualTreeHelper.GetChild(parent,i));}
 void CheckTransparentCorners(){var bitmap=new RenderTargetBitmap((int)Math.Ceiling(ActualWidth),(int)Math.Ceiling(ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(this);int stride=bitmap.PixelWidth*4;var pixels=new byte[stride*bitmap.PixelHeight];bitmap.CopyPixels(pixels,stride,0);if(pixels[3]>20||pixels[stride-1]>20||pixels[(bitmap.PixelHeight-1)*stride+3]>20||pixels[pixels.Length-1]>20)throw new Exception("Opaque pixels outside rounded card");if(pixels[Math.Min(40,bitmap.PixelHeight-1)*stride+(bitmap.PixelWidth/2)*4+3]<80)throw new Exception("Missing card surface");}
 static void PumpAnimation(int milliseconds=450){var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(milliseconds)};timer.Tick+=(s,e)=>{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
 public void CheckUi(){foreach(var q in quotas)q.Left=100;Build();UpdateLayout();CheckPercentBounds(this);CheckHover(this);CheckTransparentCorners();Point expandedButton=toggleButton.TranslatePoint(new Point(14,14),this);if(shell.ActualWidth!=280||shell.ActualHeight>250)throw new Exception("Expanded layout bounds");Menu(FindVisual<Button>(this));var panel=(FrameworkElement)settingsPopup.Child;panel.UpdateLayout();CheckHover(panel);var slider=FindVisual<Slider>(panel);if(slider==null||FindVisual<Thumb>(slider)==null)throw new Exception("Slider template");slider.Value=65;if(preferences.Transparency!=65)throw new Exception("Slider binding");slider.Value=14;settingsPopup.IsOpen=false;preferences.Compact=true;Build();UpdateLayout();CheckPercentBounds(this);if(shell.ActualWidth!=280||cardSurface.Height>75)throw new Exception("Compact layout bounds");CheckTransparentCorners();Point compactButton=toggleButton.TranslatePoint(new Point(14,14),this);if(Math.Abs(expandedButton.X-compactButton.X)>.5||Math.Abs(expandedButton.Y-compactButton.Y)>.5)throw new Exception("Toggle anchor moved");ToggleCompact();PumpAnimation();if(preferences.Compact||Math.Abs(toggleButton.TranslatePoint(new Point(14,14),this).Y-expandedButton.Y)>.5)throw new Exception("Expand animation anchor");ToggleCompact();PumpAnimation();if(!preferences.Compact||Double.IsNaN(ActualHeight))throw new Exception("Collapse animation");ToggleCompact();double fixedHeight=ActualHeight;PumpAnimation(90);if(Math.Abs(ActualHeight-fixedHeight)>.5)throw new Exception("Native window resized during morph");ToggleCompact();PumpAnimation();if(!preferences.Compact)throw new Exception("Interrupted transition state");for(int repeat=0;repeat<6;repeat++){ToggleCompact();PumpAnimation(300);CheckTransparentCorners();double expected=preferences.Compact?67:expandedCardHeight;if(Math.Abs(cardSurface.ActualHeight-expected)>1)throw new Exception("Card background collapsed");}preferences.Compact=false;Build();UpdateLayout();refreshRotation.BeginAnimation(RotateTransform.AngleProperty,new DoubleAnimation(0,360,TimeSpan.FromMilliseconds(900)){RepeatBehavior=RepeatBehavior.Forever});PumpAnimation(120);if(refreshRotation.Angle<=0)throw new Exception("Refresh rotation did not advance");StopRefreshSpin();PumpAnimation(1100);if(Math.Abs(refreshRotation.Angle)>.1)throw new Exception("Refresh rotation did not settle");Console.WriteLine("PASS: UI layout, rounded button and slider templates, transparency binding");}
}

class Program {
 [STAThread] static int Main(string[] args) {
  if(args.Contains("--check")){try{foreach(var q in QuotaClient.Read())Console.WriteLine(q.Name+": "+q.Left+"% left; reset="+q.Reset);return 0;}catch(Exception ex){Console.WriteLine(ex.Message);return 1;}}
  if(args.Contains("--diagnose")){Console.WriteLine(Native.Diagnostic());return 0;}
  try{Native.SetProcessDpiAwarenessContext(new IntPtr(-4));}catch{}
  bool preview=args.Contains("--preview")||args.Contains("--ui-check")||args.Contains("--visual-check");
  using(var mutex=new Mutex(false,preview?"Local\\CodexQuotaGlassPreview":"Local\\CodexQuotaGlass")){
   if(!mutex.WaitOne(0)){if(!preview&&!args.Contains("--background")){try{using(var signal=EventWaitHandle.OpenExisting("Local\\CodexQuotaGlass.Wake"))signal.Set();}catch(WaitHandleCannotBeOpenedException){}}return 0;}
   try{using(var wake=new EventWaitHandle(false,EventResetMode.AutoReset,preview?"Local\\CodexQuotaGlassPreview.Wake":"Local\\CodexQuotaGlass.Wake")){var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};var window=new QuotaWindow(preview,args.Contains("--compact"));window.WakeSignal=preview?null:wake;window.Start();if(!preview&&!args.Contains("--background"))window.Wake();if(preview&&!args.Contains("--visual-check")){window.Dispatcher.Invoke(()=>{},DispatcherPriority.Render);if(args.Contains("--ui-check"))window.CheckUi();else if(args.Contains("--settings"))window.ExportSettings(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"preview-settings.png"));else window.ExportPreview(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,args.Contains("--compact")?"preview-compact.png":"preview.png"));window.Close();}else app.Run();}}finally{mutex.ReleaseMutex();}
  }return 0;
 }
}
