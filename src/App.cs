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
 [DllImport("gdi32.dll")] static extern IntPtr CreateRoundRectRgn(int left,int top,int right,int bottom,int width,int height);
 [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr region);
 [DllImport("user32.dll")] static extern int SetWindowRgn(IntPtr hwnd,IntPtr region,bool redraw);
 public static void RoundWindow(IntPtr hwnd,double width,double height,double radius,double scale) {
  if(hwnd==IntPtr.Zero||width<=0||height<=0)return;
  var region=CreateRoundRectRgn(0,0,(int)Math.Ceiling(width*scale)+1,(int)Math.Ceiling(height*scale)+1,(int)(radius*2*scale),(int)(radius*2*scale));
  if(region!=IntPtr.Zero&&SetWindowRgn(hwnd,region,true)==0)DeleteObject(region);
 }
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
 [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindow callback,IntPtr p);
 delegate bool EnumWindow(IntPtr hwnd,IntPtr param);
 [DllImport("user32.dll")] static extern int GetWindowText(IntPtr h,System.Text.StringBuilder text,int length);
 [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr context);
 [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h,int attr,ref int value,int size);
 [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h,int attr,out int value,int size);
 [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr h,ref Margins m);
 [DllImport("user32.dll")] static extern int SetWindowCompositionAttribute(IntPtr h,ref Composition data);
 [StructLayout(LayoutKind.Sequential)] struct Margins {public int Left,Right,Top,Bottom;}
 [StructLayout(LayoutKind.Sequential)] struct Accent {public int State,Flags,Color,Animation;}
 [StructLayout(LayoutKind.Sequential)] struct Composition {public int Attribute;public IntPtr Data;public int Size;}
 public static uint Pid(IntPtr h){uint id;GetWindowThreadProcessId(h,out id);return id;}
 public static bool IsCodexProcess(string name,string path) {
  if(name.Equals("Codex",StringComparison.OrdinalIgnoreCase))return true;
  return name.Equals("ChatGPT",StringComparison.OrdinalIgnoreCase)&&path!=null&&(path.IndexOf("OpenAI.Codex_",StringComparison.OrdinalIgnoreCase)>=0||path.IndexOf("\\OpenAI\\Codex\\",StringComparison.OrdinalIgnoreCase)>=0);
 }
 public static bool IsCodex(IntPtr h) {
  if(h==IntPtr.Zero||!IsWindowVisible(h))return false;
  try {using(var p=Process.GetProcessById((int)Pid(h))){string name=p.ProcessName;if(name.Equals("Codex",StringComparison.OrdinalIgnoreCase))return true;if(!name.Equals("ChatGPT",StringComparison.OrdinalIgnoreCase))return false;return IsCodexProcess(name,p.MainModule.FileName);}}catch{return false;}
 }
 public static double MatchCorners(IntPtr own,IntPtr codex) {
  int preference=2;
  try {int source;if(DwmGetWindowAttribute(codex,33,out source,4)==0&&source!=0)preference=source;DwmSetWindowAttribute(own,33,ref preference,4);}catch{}
  return preference==1?0:preference==3?4:8;
 }
 public static bool Glass(IntPtr hwnd) {
  try {
   var margins=new Margins{Left=-1,Right=-1,Top=-1,Bottom=-1};DwmExtendFrameIntoClientArea(hwnd,ref margins);
   // Desktop Acrylic is native on Windows 11. Older systems use the compositor's Acrylic policy.
   int backdrop=3;if(DwmSetWindowAttribute(hwnd,38,ref backdrop,4)==0)return true;
   var accent=new Accent{State=4,Flags=2,Color=unchecked((int)0xB8FFFFFF)};
   IntPtr ptr=Marshal.AllocHGlobal(Marshal.SizeOf(accent));
   try {Marshal.StructureToPtr(accent,ptr,false);var data=new Composition{Attribute=19,Data=ptr,Size=Marshal.SizeOf(accent)};return SetWindowCompositionAttribute(hwnd,ref data)!=0;}finally{Marshal.FreeHGlobal(ptr);}
  }catch{return false;}
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
 List<WindowQuota> quotas=new List<WindowQuota>();Border shell;StackPanel body;TextBlock footerStatus,footerMessage;Grid root;
 IntPtr hwnd,lastCodex;DateTime lastUpdate;bool busy,closed,glassAvailable;double cornerRadius=28;string error;System.Windows.Forms.NotifyIcon tray;Popup settingsPopup;
 public QuotaWindow(bool isPreview,bool compactPreview) {
  preview=isPreview;preferences=preview?new Preferences{Compact=compactPreview}:Preferences.Load();
  Title="Codex Quota Glass";Width=280;SizeToContent=SizeToContent.Height;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;ShowInTaskbar=false;ShowActivated=false;Topmost=true;
  AllowsTransparency=false;Background=Brushes.Transparent;UseLayoutRounding=true;SnapsToDevicePixels=true;
  TextOptions.SetTextFormattingMode(this,TextFormattingMode.Display);TextOptions.SetTextRenderingMode(this,TextRenderingMode.ClearType);
  FontFamily=new FontFamily("Segoe UI, Microsoft YaHei UI");
  SizeChanged+=(s,e)=>ApplyRoundedRegion();
  var area=SystemParameters.WorkArea;Left=preferences.X>=area.Left&&preferences.X<area.Right-100?preferences.X:area.Right-Width-24;Top=preferences.Y>=area.Top&&preferences.Y<area.Bottom-70?preferences.Y:area.Top+70;
  SourceInitialized+=(s,e)=>{hwnd=new WindowInteropHelper(this).Handle;var source=HwndSource.FromHwnd(hwnd);source.CompositionTarget.BackgroundColor=Colors.Transparent;glassAvailable=Native.Glass(hwnd);Native.MatchCorners(hwnd,IntPtr.Zero);Build();};
  MouseLeftButtonDown+=(s,e)=>{if(e.Handled)return;if(e.ClickCount==2){ToggleCompact();return;}try{DragMove();preferences.X=Left;preferences.Y=Top;if(!preview)preferences.Save();}catch{} };
  LocationChanged+=(s,e)=>{if(!preview&&!closed){preferences.X=Left;preferences.Y=Top;if(!preview)preferences.Save();}};
  Closed+=(s,e)=>{closed=true;if(settingsPopup!=null)settingsPopup.IsOpen=false;shutdown.Cancel();foreground.Stop();refresh.Stop();countdown.Stop();if(tray!=null)tray.Dispose();shutdown.Dispose();Application.Current.Shutdown();};
  foreground.Tick+=(s,e)=>UpdateVisibility();refresh.Tick+=(s,e)=>Refresh();countdown.Tick+=(s,e)=>UpdateCountdown();
  if(preview){lastUpdate=DateTime.Now;quotas=new List<WindowQuota>{new WindowQuota{Name="5 小时",Left=82,Reset=DateTimeOffset.UtcNow.ToUnixTimeSeconds()+7140},new WindowQuota{Name="每周",Left=64,Reset=DateTimeOffset.UtcNow.ToUnixTimeSeconds()+176400}};}
  else {
   tray=new System.Windows.Forms.NotifyIcon{Icon=System.Drawing.SystemIcons.Information,Text="Codex 额度悬浮窗",Visible=true};
   var menu=new System.Windows.Forms.ContextMenuStrip();
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
 Button IconButton(string tooltip,string geometry,Action click) {
  var drawing=new System.Windows.Shapes.Path{Data=Geometry.Parse(geometry),Stroke=Ink("#79838E"),StrokeThickness=1.3,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,Width=14,Height=14,Stretch=Stretch.Uniform};
  var button=new Button{Content=drawing,ToolTip=tooltip,Width=26,Height=26,Background=Brushes.Transparent,BorderThickness=new Thickness(0),Padding=new Thickness(7),Cursor=Cursors.Hand,Focusable=false};
  RoundedButton(button);
  button.Click+=(s,e)=>{click();e.Handled=true;};return button;
 }
 static void Animate(UIElement target,DependencyProperty property,double value,int milliseconds){var animation=new DoubleAnimation(value,TimeSpan.FromMilliseconds(SystemParameters.ClientAreaAnimation?milliseconds:0)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut}};target.BeginAnimation(property,animation);}
 void RoundedButton(Button button) {
  var frame=new FrameworkElementFactory(typeof(Border));frame.Name="Surface";frame.SetValue(Border.CornerRadiusProperty,new CornerRadius(10));frame.SetValue(Border.BackgroundProperty,new SolidColorBrush(Color.FromArgb(0,255,255,255)));
  var presenter=new FrameworkElementFactory(typeof(ContentPresenter));presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty,HorizontalAlignment.Center);presenter.SetValue(ContentPresenter.VerticalAlignmentProperty,VerticalAlignment.Center);frame.AppendChild(presenter);
  button.Template=new ControlTemplate(typeof(Button)){VisualTree=frame};button.RenderTransformOrigin=new Point(.5,.5);var scale=new ScaleTransform(1,1);button.RenderTransform=scale;
  Action<double,int> zoom=(v,ms)=>{var a=new DoubleAnimation(v,TimeSpan.FromMilliseconds(SystemParameters.ClientAreaAnimation?ms:0)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut}};scale.BeginAnimation(ScaleTransform.ScaleXProperty,a);scale.BeginAnimation(ScaleTransform.ScaleYProperty,a);};
  Action<byte> tint=a=>{button.ApplyTemplate();var border=button.Template.FindName("Surface",button) as Border;if(border!=null){var brush=border.Background as SolidColorBrush;if(brush!=null)brush.BeginAnimation(SolidColorBrush.ColorProperty,new ColorAnimation(Color.FromArgb(a,255,255,255),TimeSpan.FromMilliseconds(SystemParameters.ClientAreaAnimation?120:0)));}};
  button.MouseEnter+=(s,e)=>tint(170);button.MouseLeave+=(s,e)=>{tint(0);zoom(1,180);};button.PreviewMouseLeftButtonDown+=(s,e)=>{tint(230);zoom(.96,80);};button.PreviewMouseLeftButtonUp+=(s,e)=>zoom(1,180);
 }
 void ApplyRoundedRegion(){if(preview)return;var source=PresentationSource.FromVisual(this);double scale=source==null?1:source.CompositionTarget.TransformToDevice.M11;Native.RoundWindow(hwnd,ActualWidth,ActualHeight,cornerRadius,scale);}
 void ToggleCompact(){BeginAnimation(HeightProperty,null);if(settingsPopup!=null)settingsPopup.IsOpen=false;double oldHeight=ActualHeight;preferences.Compact=!preferences.Compact;if(!preview)preferences.Save();Build();UpdateLayout();double target=ActualHeight;if(!SystemParameters.ClientAreaAnimation||oldHeight<=0)return;SizeToContent=SizeToContent.Manual;Height=target;var a=new DoubleAnimation(oldHeight,target,TimeSpan.FromMilliseconds(220)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut}};a.Completed+=(s,e)=>{BeginAnimation(HeightProperty,null);Height=Double.NaN;SizeToContent=SizeToContent.Height;ApplyRoundedRegion();};BeginAnimation(HeightProperty,a);}
 void Menu(Button anchor) {
  if(settingsPopup!=null&&settingsPopup.IsOpen){CloseSettings();return;}
  var content=new StackPanel();var title=new Grid();title.ColumnDefinitions.Add(new ColumnDefinition());title.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});title.Children.Add(Text("透明度",13,"#26313B",FontWeights.SemiBold));var close=IconButton("关闭设置","M 3,3 L 11,11 M 11,3 L 3,11",CloseSettings);Grid.SetColumn(close,1);title.Children.Add(close);content.Children.Add(title);
  var value=Text(preferences.Transparency.ToString("0")+"%",11,"#75818C");value.Margin=new Thickness(0,6,0,10);content.Children.Add(value);
  var slider=new Slider{Minimum=5,Maximum=65,Value=Math.Max(5,Math.Min(65,preferences.Transparency)),SmallChange=1,LargeChange=5,TickFrequency=1,IsSnapToTickEnabled=true,Height=24,ToolTip="仅改变玻璃背景透明度"};
  slider.Template=(ControlTemplate)System.Windows.Markup.XamlReader.Parse(@"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='{x:Type Slider}' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'><Grid Height='24'><Border Height='4' CornerRadius='2' Background='#DAE0E6'/><Track x:Name='PART_Track' Minimum='{TemplateBinding Minimum}' Maximum='{TemplateBinding Maximum}' Value='{TemplateBinding Value}'><Track.DecreaseRepeatButton><RepeatButton Command='Slider.DecreaseLarge' Focusable='False'><RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Border Height='4' CornerRadius='2' Background='#8294AA'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton><Track.Thumb><Thumb Width='20' Height='20' Cursor='Hand'><Thumb.Template><ControlTemplate TargetType='Thumb'><Border CornerRadius='10' Background='White' BorderBrush='#D9DFE5' BorderThickness='.7'/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb><Track.IncreaseRepeatButton><RepeatButton Command='Slider.IncreaseLarge' Focusable='False'><RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton></Track></Grid></ControlTemplate>");
  slider.ValueChanged+=(s,e)=>{preferences.Transparency=e.NewValue;value.Text=e.NewValue.ToString("0")+"%";UpdateGlass();};slider.AddHandler(Thumb.DragCompletedEvent,new DragCompletedEventHandler((s,e)=>{if(!preview)preferences.Save();}));slider.LostKeyboardFocus+=(s,e)=>{if(!preview)preferences.Save();};content.Children.Add(slider);
  var only=new CheckBox{Content="仅 Codex 前台显示",IsChecked=preferences.ForegroundOnly,FontSize=11,Foreground=Ink("#66737F"),Margin=new Thickness(0,12,0,0)};only.Click+=(s,e)=>{preferences.ForegroundOnly=only.IsChecked==true;if(!preview)preferences.Save();UpdateVisibility();};content.Children.Add(only);
  var panel=new Border{Width=240,Padding=new Thickness(16,12,16,16),CornerRadius=new CornerRadius(22),Background=new SolidColorBrush(Color.FromArgb(245,248,250,252)),BorderBrush=Brushes.White,BorderThickness=new Thickness(1),Child=content};
  settingsPopup=new Popup{Child=panel,PlacementTarget=anchor,Placement=PlacementMode.Bottom,HorizontalOffset=-208,VerticalOffset=8,AllowsTransparency=true,StaysOpen=false,PopupAnimation=PopupAnimation.None};settingsPopup.Closed+=(s,e)=>{if(!preview)preferences.Save();};settingsPopup.IsOpen=true;
  panel.Opacity=0;var shift=new TranslateTransform(0,-6);panel.RenderTransform=shift;Animate(panel,OpacityProperty,1,180);shift.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(0,TimeSpan.FromMilliseconds(SystemParameters.ClientAreaAnimation?180:0)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut}});
 }
 void CloseSettings(){if(settingsPopup==null||!settingsPopup.IsOpen)return;var popup=settingsPopup;var panel=popup.Child;var a=new DoubleAnimation(0,TimeSpan.FromMilliseconds(SystemParameters.ClientAreaAnimation?140:0));a.Completed+=(s,e)=>popup.IsOpen=false;panel.BeginAnimation(OpacityProperty,a);if(!preview)preferences.Save();}
 void UpdateGlass(){if(shell!=null)shell.Background=preview?new SolidColorBrush(Color.FromRgb(239,244,246)):glassAvailable?new SolidColorBrush(Color.FromArgb((byte)(255*(1-Math.Max(5,Math.Min(65,preferences.Transparency))/100)),255,255,255)):Brushes.White;}
 void Build() {
  if(settingsPopup!=null)settingsPopup.IsOpen=false;countdownLabels.Clear();var area=SystemParameters.WorkArea;Width=preferences.Compact?268:280;cornerRadius=preferences.Compact?34:28;
  shell=new Border{CornerRadius=new CornerRadius(cornerRadius),BorderBrush=new SolidColorBrush(Color.FromArgb(210,255,255,255)),BorderThickness=new Thickness(1),Padding=preferences.Compact?new Thickness(18,12,18,12):new Thickness(18,15,18,14)};
  UpdateGlass();
  root=new Grid();body=new StackPanel();root.Children.Add(body);shell.Child=root;Content=shell;
  if(preferences.Compact){BuildCompact();UpdateFooter();return;}
  var header=new Grid{Margin=new Thickness(0,0,0,10)};header.ColumnDefinitions.Add(new ColumnDefinition());header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
  header.Children.Add(Text("codex",14,"#26313B",FontWeights.SemiBold));
  var actions=new StackPanel{Orientation=Orientation.Horizontal};Grid.SetColumn(actions,1);header.Children.Add(actions);
  actions.Children.Add(IconButton("刷新额度","M 12,5 A 5,5 0 1 0 12,10 M 12,1 L 12,5 L 8,5",Refresh));actions.Children.Add(IconButton("收起","M 2,9 L 7,4 L 12,9",ToggleCompact));
  Button more=null;more=IconButton("设置","M 1,7 L 2,7 M 6,7 L 7,7 M 11,7 L 12,7",()=>Menu(more));actions.Children.Add(more);body.Children.Add(header);
  if(quotas.Count==0)body.Children.Add(Text(busy?"正在查询额度…":error??"等待额度数据",12,"#75818C"));
  for(int i=0;i<quotas.Count;i++) {
   var q=quotas[i];var row=new Grid();row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
   var label=Text(q.Name,12,"#65727E");label.ToolTip=q.Name;row.Children.Add(label);
   var percent=Text(q.Left.ToString("0.#",CultureInfo.InvariantCulture)+"%",27,q.Left<=10?"#AE5E66":"#26313B",FontWeights.Medium);Grid.SetColumn(percent,1);row.Children.Add(percent);body.Children.Add(row);
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
  else {SizeToContent=SizeToContent.Height;Height=Double.NaN;}
 }
 void BuildCompact() {
  var row=new Grid();row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(48)});row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(26)});
  var brand=new StackPanel{VerticalAlignment=VerticalAlignment.Center};brand.Children.Add(Text("codex",12,"#26313B",FontWeights.SemiBold));row.Children.Add(brand);
  var metrics=new UniformGrid{Rows=1,Columns=Math.Max(1,quotas.Count)};Grid.SetColumn(metrics,1);row.Children.Add(metrics);
  foreach(var q in quotas){var metric=new StackPanel{Margin=new Thickness(16,0,0,0)};var label=Text(q.Name,9,"#7A8792");label.ToolTip=q.Name;metric.Children.Add(label);metric.Children.Add(Text(q.Left.ToString("0.#")+"%",21,"#26313B",FontWeights.Medium));metrics.Children.Add(metric);}
  if(quotas.Count==0)metrics.Children.Add(Text("加载中",12,"#7A8792"));
  var expand=IconButton("展开","M 2,9 L 7,4 L 12,9",ToggleCompact);Grid.SetColumn(expand,2);row.Children.Add(expand);body.Children.Add(row);
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
  if(busy||closed||preview)return;busy=true;UpdateFooter();
  try {var data=await Task.Run(()=>QuotaClient.Read());if(closed)return;quotas=data;lastUpdate=DateTime.Now;error=null;Build();}
  catch(Exception ex){if(!closed){error=ex.Message;Build();}}
  finally {busy=false;if(!closed)UpdateFooter();}
 }
 void UpdateVisibility() {
  if(closed||preview)return;IntPtr current=Native.GetForegroundWindow();bool codex=Native.IsCodex(current);
  bool own=current!=IntPtr.Zero&&Native.Pid(current)==(uint)Process.GetCurrentProcess().Id;
  if(codex&&lastCodex!=current){lastCodex=current;if(hwnd!=IntPtr.Zero){ApplyRoundedRegion();}}
  bool show=!preferences.ForegroundOnly||codex||(own&&IsVisible&&lastCodex!=IntPtr.Zero);
  if(show&&!IsVisible)Show();else if(!show&&IsVisible){if(settingsPopup!=null)settingsPopup.IsOpen=false;Hide();}
 }
 public void Start(){if(preview){Show();return;}new WindowInteropHelper(this).EnsureHandle();UpdateVisibility();}
 public void ExportPreview(string path){UpdateLayout();var image=new RenderTargetBitmap((int)(ActualWidth*3),(int)(ActualHeight*3),288,288,PixelFormats.Pbgra32);image.Render(this);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using(var stream=File.Create(path))encoder.Save(stream);}
 public void ExportSettings(string path){var anchor=FindVisual<Button>(this);Menu(anchor);var panel=(FrameworkElement)settingsPopup.Child;panel.BeginAnimation(OpacityProperty,null);panel.Opacity=1;panel.RenderTransform=Transform.Identity;panel.UpdateLayout();var image=new RenderTargetBitmap((int)(panel.ActualWidth*3),(int)(panel.ActualHeight*3),288,288,PixelFormats.Pbgra32);image.Render(panel);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using(var stream=File.Create(path))encoder.Save(stream);settingsPopup.IsOpen=false;}
 static T FindVisual<T>(DependencyObject parent) where T:DependencyObject {for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var child=VisualTreeHelper.GetChild(parent,i);if(child is T)return (T)child;var found=FindVisual<T>(child);if(found!=null)return found;}return null;}
 public void CheckUi(){UpdateLayout();if(ActualWidth!=280||ActualHeight>250)throw new Exception("Expanded layout bounds");Menu(FindVisual<Button>(this));var panel=(FrameworkElement)settingsPopup.Child;panel.UpdateLayout();var slider=FindVisual<Slider>(panel);if(slider==null||FindVisual<Thumb>(slider)==null)throw new Exception("Slider template");slider.Value=65;if(preferences.Transparency!=65)throw new Exception("Slider binding");slider.Value=14;settingsPopup.IsOpen=false;preferences.Compact=true;Build();UpdateLayout();if(ActualWidth!=268||ActualHeight>75)throw new Exception("Compact layout bounds");Console.WriteLine("PASS: UI layout, rounded button and slider templates, transparency binding");}
}

class Program {
 [STAThread] static int Main(string[] args) {
  if(args.Contains("--check")){try{foreach(var q in QuotaClient.Read())Console.WriteLine(q.Name+": "+q.Left+"% left; reset="+q.Reset);return 0;}catch(Exception ex){Console.WriteLine(ex.Message);return 1;}}
  if(args.Contains("--diagnose")){Console.WriteLine(Native.Diagnostic());return 0;}
  try{Native.SetProcessDpiAwarenessContext(new IntPtr(-4));}catch{}
  bool preview=args.Contains("--preview")||args.Contains("--ui-check");
  using(var mutex=new Mutex(false,preview?"Local\\CodexQuotaGlassPreview":"Local\\CodexQuotaGlass")){
   if(!mutex.WaitOne(0))return 0;
   try{var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};var window=new QuotaWindow(preview,args.Contains("--compact"));window.Start();if(preview){window.Dispatcher.Invoke(()=>{},DispatcherPriority.Render);if(args.Contains("--ui-check"))window.CheckUi();else if(args.Contains("--settings"))window.ExportSettings(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"preview-settings.png"));else window.ExportPreview(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,args.Contains("--compact")?"preview-compact.png":"preview.png"));window.Close();}else app.Run();}finally{mutex.ReleaseMutex();}
  }return 0;
 }
}
