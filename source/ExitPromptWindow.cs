using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace VitaPad;
public sealed class ExitPromptWindow : Window
{
    public event Action? CancelRequested;
    public bool AllowClose { get; set; }
    public ExitPromptWindow()
    {
        Title="VitaPad · Exit?";
        Width=460; Height=250; ResizeMode=ResizeMode.NoResize;
        WindowStartupLocation=WindowStartupLocation.CenterScreen;
        ShowInTaskbar=false; Topmost=true; ShowActivated=false;
        Background=MainWindow.Brush("#141617"); Foreground=MainWindow.Brush("#F3F2EF");
        FontFamily=new("Segoe UI");
        var panel=new StackPanel{Margin=new Thickness(28)};
        panel.Children.Add(new TextBlock{Text="Exit VitaPad?",FontSize=27,FontWeight=FontWeights.SemiBold});
        panel.Children.Add(new TextBlock{Text="Release the face buttons, then press:",FontSize=14,Margin=new Thickness(0,10,0,18),Foreground=MainWindow.Brush("#B7BDC2")});
        var row=new Grid();row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new());
        var yes=new TextBlock{Text="×   Exit",FontSize=24,Foreground=MainWindow.Brush("#F26972"),VerticalAlignment=VerticalAlignment.Center};row.Children.Add(yes);
        var no=new Button{Content="○   Cancel",FontSize=21,Padding=new Thickness(12,7,12,7),Background=MainWindow.Brush("#292E33"),Foreground=Foreground,BorderBrush=MainWindow.Brush("#4A5056")};no.Click+=(_,_)=>CancelRequested?.Invoke();Grid.SetColumn(no,1);row.Children.Add(no);
        panel.Children.Add(row);Content=panel;
        Closing+=(_,e)=>{if(!AllowClose){e.Cancel=true;CancelRequested?.Invoke();Hide();}};
        PreviewKeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Escape){e.Handled=true;CancelRequested?.Invoke();}};
    }
}
