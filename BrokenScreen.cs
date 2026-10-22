// 屏幕坏掉特效：启动时截取桌面，全屏覆盖后在截图上叠加各种故障效果。
// exe 旁边「素材」文件夹里的图片可以叠加上去。按 Esc 退出。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

static class Program
{
    [DllImport("user32.dll")]
    static extern bool SetProcessDPIAware();

    [STAThread]
    static void Main(string[] args)
    {
        try { SetProcessDPIAware(); } catch { }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        GlitchSettings settings = GlitchSettings.Load();

        // 带参数时跳过界面，用上次保存的设置直接发作：BrokenScreen.exe 10 表示 10 秒后发作
        int delay;
        if (args.Length > 0 && int.TryParse(args[0], out delay))
        {
            if (delay > 0) System.Threading.Thread.Sleep(delay * 1000);
            Application.Run(new BrokenScreenForm(settings));
        }
        else
        {
            Application.Run(new LauncherForm(settings));
        }
    }
}

enum BlendMode { Normal, RemoveBlack, RemoveWhite }
enum FitMode { Cover, Stretch, Center, Random }

class MaterialSpec
{
    public string Name;                   // 素材文件夹里的文件名
    public bool On = true;
    public BlendMode Blend;
    public FitMode Fit;
    public int Opacity = 100;             // 5..100
}

class GlitchSettings
{
    public static readonly string BaseDir = AppDomain.CurrentDomain.BaseDirectory;
    public static readonly string MaterialDir = Path.Combine(BaseDir, "素材");
    static readonly string FilePath = Path.Combine(BaseDir, "settings.ini");

    public int Level = 3;                 // 1..5
    public bool[] Modes = { false, true, true, true, true, true, true, true, true, true }; // 下标 1..9 对应各故障模式
    public bool DeadLines = true;
    public bool Crack = true;
    public int Delay;                     // 秒
    public int AutoClose;                 // 秒，0 = 不自动结束
    public List<MaterialSpec> Materials = new List<MaterialSpec>();

    public static GlitchSettings Load()
    {
        GlitchSettings s = new GlitchSettings();
        try
        {
            if (!File.Exists(FilePath)) return s;
            foreach (string line in File.ReadAllLines(FilePath, Encoding.UTF8))
            {
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string k = line.Substring(0, eq), v = line.Substring(eq + 1);
                switch (k)
                {
                    case "level": s.Level = Clamp(int.Parse(v), 1, 5); break;
                    case "modes":
                        for (int i = 1; i <= 9 && i <= v.Length; i++) s.Modes[i] = v[i - 1] == '1';
                        break;
                    case "deadlines": s.DeadLines = v == "1"; break;
                    case "crack": s.Crack = v == "1"; break;
                    case "delay": s.Delay = Clamp(int.Parse(v), 0, 3600); break;
                    case "autoclose": s.AutoClose = Clamp(int.Parse(v), 0, 3600); break;
                    case "material":
                        string[] p = v.Split('|');
                        if (p.Length < 5) break;
                        MaterialSpec m = new MaterialSpec();
                        m.Name = p[0];
                        m.On = p[1] == "1";
                        m.Blend = (BlendMode)Clamp(int.Parse(p[2]), 0, 2);
                        m.Fit = (FitMode)Clamp(int.Parse(p[3]), 0, 3);
                        m.Opacity = Clamp(int.Parse(p[4]), 5, 100);
                        s.Materials.Add(m);
                        break;
                }
            }
        }
        catch { }  // 设置文件损坏时，读不了的部分用默认值
        return s;
    }

    public void Save()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("level=" + Level);
        sb.Append("modes=");
        for (int i = 1; i <= 9; i++) sb.Append(Modes[i] ? '1' : '0');
        sb.AppendLine();
        sb.AppendLine("deadlines=" + (DeadLines ? 1 : 0));
        sb.AppendLine("crack=" + (Crack ? 1 : 0));
        sb.AppendLine("delay=" + Delay);
        sb.AppendLine("autoclose=" + AutoClose);
        foreach (MaterialSpec m in Materials)
            sb.AppendLine("material=" + m.Name + "|" + (m.On ? 1 : 0) + "|" + (int)m.Blend + "|" + (int)m.Fit + "|" + m.Opacity);
        try { File.WriteAllText(FilePath, sb.ToString(), Encoding.UTF8); } catch { }
    }

    static int Clamp(int v, int min, int max) { return Math.Max(min, Math.Min(max, v)); }
}

static class Pixels
{
    public static int[] Read(Bitmap b, PixelFormat format)
    {
        int[] px = new int[b.Width * b.Height];
        BitmapData d = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadOnly, format);
        Marshal.Copy(d.Scan0, px, 0, px.Length);
        b.UnlockBits(d);
        return px;
    }

    public static void Write(Bitmap b, int[] px)
    {
        BitmapData d = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
        Marshal.Copy(px, 0, d.Scan0, px.Length);
        b.UnlockBits(d);
    }
}

static class Materials
{
    public static readonly string[] BlendNames = { "正常", "去黑底", "去白底" };
    public static readonly string[] FitNames = { "铺满全屏", "拉伸全屏", "原尺寸居中", "原尺寸随机" };
    static readonly string[] Exts = { ".png", ".jpg", ".jpeg", ".bmp", ".gif" };

    public static List<string> Scan()
    {
        List<string> names = new List<string>();
        try
        {
            Directory.CreateDirectory(GlitchSettings.MaterialDir);
            foreach (string f in Directory.GetFiles(GlitchSettings.MaterialDir))
                if (Array.IndexOf(Exts, Path.GetExtension(f).ToLowerInvariant()) >= 0)
                    names.Add(Path.GetFileName(f));
        }
        catch { }
        names.Sort(StringComparer.CurrentCultureIgnoreCase);
        return names;
    }

    // 读进内存再解码，避免占用文件
    static Bitmap Load(string name)
    {
        byte[] data = File.ReadAllBytes(Path.Combine(GlitchSettings.MaterialDir, name));
        using (MemoryStream ms = new MemoryStream(data))
        using (Image img = Image.FromStream(ms))
        {
            Bitmap b = new Bitmap(img.Width, img.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(b))
                g.DrawImage(img, new Rectangle(0, 0, img.Width, img.Height));
            return b;
        }
    }

    // 根据图片猜叠加方式：带透明的 PNG 用正常，暗图去黑底，亮图去白底
    public static BlendMode Detect(string name)
    {
        try
        {
            using (Bitmap full = Load(name))
            using (Bitmap small = new Bitmap(full, 64, 64))
            {
                int[] px = Pixels.Read(small, PixelFormat.Format32bppArgb);
                int transparent = 0;
                long lum = 0;
                foreach (int c in px)
                {
                    if (((c >> 24) & 255) < 250) transparent++;
                    lum += (((c >> 16) & 255) * 3 + ((c >> 8) & 255) * 6 + (c & 255)) / 10;
                }
                if (transparent > px.Length / 100) return BlendMode.Normal;
                double avg = lum / (double)px.Length / 255;
                if (avg < 0.3) return BlendMode.RemoveBlack;
                if (avg > 0.7) return BlendMode.RemoveWhite;
                return BlendMode.Normal;
            }
        }
        catch { return BlendMode.Normal; }
    }

    // 把勾选的素材按各自设置合成为一张全屏透明图层
    public static Bitmap BuildOverlay(List<MaterialSpec> specs, int W, int H, Random rnd)
    {
        Bitmap ov = null;
        foreach (MaterialSpec m in specs)
        {
            if (!m.On) continue;
            try
            {
                using (Bitmap src = Load(m.Name))
                using (Bitmap layer = ToLayer(src, m))
                using (ImageAttributes ia = new ImageAttributes())
                {
                    if (ov == null) ov = new Bitmap(W, H, PixelFormat.Format32bppPArgb);
                    ia.SetWrapMode(WrapMode.TileFlipXY);   // 缩放时边缘不出现杂边
                    using (Graphics g = Graphics.FromImage(ov))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.DrawImage(layer, Place(m.Fit, layer.Width, layer.Height, W, H, rnd),
                            0, 0, layer.Width, layer.Height, GraphicsUnit.Pixel, ia);
                    }
                }
            }
            catch { }  // 读不了的图片直接跳过
        }
        return ov;
    }

    // 把叠加方式换算成预乘透明度，之后每帧只需普通的透明叠加：
    // 去黑底 ≈ 滤色（越亮越不透明），去白底 ≈ 正片叠底（越暗越不透明）
    static Bitmap ToLayer(Bitmap src, MaterialSpec m)
    {
        int[] px = Pixels.Read(src, PixelFormat.Format32bppArgb);
        int op = m.Opacity * 255 / 100;
        for (int i = 0; i < px.Length; i++)
        {
            int c = px[i];
            int a = (c >> 24) & 255, r = (c >> 16) & 255, g = (c >> 8) & 255, b = c & 255;
            int alpha;
            if (m.Blend == BlendMode.RemoveBlack)
            {
                alpha = Math.Max(r, Math.Max(g, b));
            }
            else if (m.Blend == BlendMode.RemoveWhite)
            {
                int min = Math.Min(r, Math.Min(g, b));
                alpha = 255 - min;
                r -= min; g -= min; b -= min;
            }
            else
            {
                alpha = 255;
            }
            int k = a * op / 255;  // 原图自身透明度 × 不透明度
            alpha = alpha * k / 255;
            r = r * k / 255; g = g * k / 255; b = b * k / 255;
            px[i] = (alpha << 24) | (r << 16) | (g << 8) | b;
        }
        Bitmap layer = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppPArgb);
        Pixels.Write(layer, px);
        return layer;
    }

    static Rectangle Place(FitMode fit, int w, int h, int W, int H, Random rnd)
    {
        switch (fit)
        {
            case FitMode.Stretch:
                return new Rectangle(0, 0, W, H);
            case FitMode.Center:
                return new Rectangle((W - w) / 2, (H - h) / 2, w, h);
            case FitMode.Random:
                // 至少保留三分之一在屏幕内
                return new Rectangle(
                    rnd.Next(-w / 3, Math.Max(-w / 3 + 1, W - w * 2 / 3)),
                    rnd.Next(-h / 3, Math.Max(-h / 3 + 1, H - h * 2 / 3)), w, h);
            default:
                double s = Math.Max((double)W / w, (double)H / h);
                int dw = (int)Math.Ceiling(w * s), dh = (int)Math.Ceiling(h * s);
                return new Rectangle((W - dw) / 2, (H - dh) / 2, dw, dh);
        }
    }
}

class LauncherForm : Form
{
    static readonly string[] ModeNames =
    {
        null, "画面撕裂", "色差错位", "电视彩条", "雪花噪点", "彩色条纹",
        "反色闪烁", "画面滚动", "马赛克花屏", "关机黑屏"
    };
    static readonly string[] LevelNames = { null, "轻微", "一般", "严重", "很严重", "彻底报废" };
    static readonly string[] LevelTips =
    {
        null, "偶尔闪一下，像接触不良", "时不时出点毛病", "频繁故障",
        "大部分时间都在花屏", "几乎没有正常画面"
    };

    readonly GlitchSettings cfg;
    readonly float s;
    readonly TrackBar level;
    readonly Label levelName, levelTip;
    readonly CheckBox[] modeBoxes = new CheckBox[10];
    readonly CheckBox deadLines, crackBox;
    readonly NumericUpDown delay, autoClose;
    readonly ListView matList;
    readonly ComboBox blendBox, fitBox;
    readonly NumericUpDown opacity;
    bool syncing;

    public LauncherForm(GlitchSettings settings)
    {
        cfg = settings;
        Text = "屏幕坏掉特效";
        Font = new Font("Microsoft YaHei UI", 9f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        using (Graphics g = CreateGraphics()) s = g.DpiX / 96f;

        FlowLayoutPanel root = Panel(FlowDirection.TopDown);
        root.Padding = new Padding(Px(16), Px(4), Px(16), Px(16));
        Controls.Add(root);

        // 程度
        root.Controls.Add(Header("损坏程度"));
        level = new TrackBar();
        level.Minimum = 1;
        level.Maximum = 5;
        level.LargeChange = 1;
        level.Value = cfg.Level;
        level.Width = Px(300);
        levelName = new Label();
        levelName.AutoSize = true;
        levelName.Font = new Font(Font, FontStyle.Bold);
        levelName.ForeColor = Color.Firebrick;
        levelName.Margin = new Padding(Px(8), Px(8), 0, 0);
        levelTip = new Label();
        levelTip.AutoSize = true;
        levelTip.ForeColor = SystemColors.GrayText;
        level.ValueChanged += delegate { UpdateLevel(); };
        root.Controls.Add(Row(level, levelName));
        root.Controls.Add(levelTip);
        UpdateLevel();

        // 效果
        root.Controls.Add(Header("效果"));
        TableLayoutPanel grid = new TableLayoutPanel();
        grid.ColumnCount = 3;
        grid.AutoSize = true;
        for (int i = 1; i <= 9; i++)
        {
            modeBoxes[i] = Check(ModeNames[i], cfg.Modes[i]);
            grid.Controls.Add(modeBoxes[i]);
        }
        deadLines = Check("坏线", cfg.DeadLines);
        crackBox = Check("程序画的裂纹", cfg.Crack);
        grid.Controls.Add(deadLines);
        grid.Controls.Add(crackBox);
        root.Controls.Add(grid);

        Button all = SmallButton("全选"), none = SmallButton("全不选");
        all.Click += delegate { SetAll(true); };
        none.Click += delegate { SetAll(false); };
        root.Controls.Add(Row(all, none));

        // 素材
        root.Controls.Add(Header("素材（打勾的会叠加在屏幕上，一直显示）"));
        matList = new ListView();
        matList.View = View.Details;
        matList.CheckBoxes = true;
        matList.FullRowSelect = true;
        matList.HideSelection = false;
        matList.MultiSelect = false;
        matList.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        matList.Size = new Size(Px(460), Px(130));
        matList.Columns.Add("文件", Px(200));
        matList.Columns.Add("叠加方式", Px(70));
        matList.Columns.Add("位置", Px(90));
        matList.Columns.Add("不透明度", Px(70));
        matList.ItemChecked += delegate(object o, ItemCheckedEventArgs e)
        {
            MaterialSpec m = e.Item.Tag as MaterialSpec;
            if (m != null) m.On = e.Item.Checked;
        };
        matList.SelectedIndexChanged += delegate { ShowSelected(); };
        root.Controls.Add(matList);

        blendBox = Combo(Materials.BlendNames);
        fitBox = Combo(Materials.FitNames);
        opacity = Number();
        opacity.Minimum = 5;
        opacity.Maximum = 100;
        opacity.Increment = 5;
        blendBox.SelectedIndexChanged += delegate { EditSelected(); };
        fitBox.SelectedIndexChanged += delegate { EditSelected(); };
        opacity.ValueChanged += delegate { EditSelected(); };
        root.Controls.Add(Row(Caption("选中项："), blendBox, fitBox, Caption("不透明度"), opacity, Caption("%")));

        Button openDir = SmallButton("打开素材文件夹"), refresh = SmallButton("刷新");
        openDir.Click += delegate
        {
            Directory.CreateDirectory(GlitchSettings.MaterialDir);
            Process.Start("explorer.exe", "\"" + GlitchSettings.MaterialDir + "\"");
        };
        refresh.Click += delegate { RefreshMaterials(); };
        root.Controls.Add(Row(openDir, refresh));

        Label matTip = Caption("把 png / jpg 放进「素材」文件夹后点刷新，按文件名顺序叠加，排在后面的盖在上面。"
            + "黑底的裂纹图选「去黑底」，白底的污渍图选「去白底」，透明 PNG 或整张照片选「正常」。");
        matTip.ForeColor = SystemColors.GrayText;
        matTip.MaximumSize = new Size(Px(460), 0);
        root.Controls.Add(matTip);
        RefreshMaterials();

        // 时间
        root.Controls.Add(Header("时间"));
        delay = Number();
        autoClose = Number();
        delay.Value = cfg.Delay;
        autoClose.Value = cfg.AutoClose;
        root.Controls.Add(Row(Caption("延迟"), delay, Caption("秒后发作")));
        root.Controls.Add(Row(Caption("持续"), autoClose, Caption("秒后自动恢复（0 = 一直坏下去）")));

        Button start = new Button();
        start.Text = "开始";
        start.AutoSize = true;
        start.Font = new Font(Font.FontFamily, 11f, FontStyle.Bold);
        start.Padding = new Padding(Px(24), Px(2), Px(24), Px(2));
        start.Margin = new Padding(0, Px(16), 0, Px(4));
        start.Click += delegate { Launch(); };
        AcceptButton = start;
        root.Controls.Add(start);

        Label hint = Caption("运行时按 Esc 随时退出");
        hint.ForeColor = SystemColors.GrayText;
        root.Controls.Add(hint);
    }

    void UpdateLevel()
    {
        levelName.Text = LevelNames[level.Value];
        levelTip.Text = LevelTips[level.Value];
    }

    void SetAll(bool on)
    {
        for (int i = 1; i <= 9; i++) modeBoxes[i].Checked = on;
        deadLines.Checked = on;
        crackBox.Checked = on;
    }

    void RefreshMaterials()
    {
        Dictionary<string, MaterialSpec> known = new Dictionary<string, MaterialSpec>(StringComparer.OrdinalIgnoreCase);
        foreach (MaterialSpec m in cfg.Materials) known[m.Name] = m;

        List<MaterialSpec> list = new List<MaterialSpec>();
        foreach (string name in Materials.Scan())
        {
            MaterialSpec m;
            if (!known.TryGetValue(name, out m))
            {
                m = new MaterialSpec();
                m.Name = name;
                m.Blend = Materials.Detect(name);
            }
            list.Add(m);
        }
        cfg.Materials = list;

        matList.BeginUpdate();
        matList.Items.Clear();
        foreach (MaterialSpec m in list)
        {
            ListViewItem it = new ListViewItem(m.Name);
            it.SubItems.Add("");
            it.SubItems.Add("");
            it.SubItems.Add("");
            it.Tag = m;
            it.Checked = m.On;
            UpdateRow(it);
            matList.Items.Add(it);
        }
        matList.EndUpdate();
        if (matList.Items.Count > 0) matList.Items[0].Selected = true;
        ShowSelected();
    }

    MaterialSpec SelectedMaterial()
    {
        return matList.SelectedItems.Count > 0 ? (MaterialSpec)matList.SelectedItems[0].Tag : null;
    }

    void ShowSelected()
    {
        MaterialSpec m = SelectedMaterial();
        syncing = true;
        blendBox.Enabled = fitBox.Enabled = opacity.Enabled = m != null;
        if (m != null)
        {
            blendBox.SelectedIndex = (int)m.Blend;
            fitBox.SelectedIndex = (int)m.Fit;
            opacity.Value = m.Opacity;
        }
        syncing = false;
    }

    void EditSelected()
    {
        MaterialSpec m = SelectedMaterial();
        if (syncing || m == null) return;
        m.Blend = (BlendMode)blendBox.SelectedIndex;
        m.Fit = (FitMode)fitBox.SelectedIndex;
        m.Opacity = (int)opacity.Value;
        UpdateRow(matList.SelectedItems[0]);
    }

    static void UpdateRow(ListViewItem it)
    {
        MaterialSpec m = (MaterialSpec)it.Tag;
        it.SubItems[1].Text = Materials.BlendNames[(int)m.Blend];
        it.SubItems[2].Text = Materials.FitNames[(int)m.Fit];
        it.SubItems[3].Text = m.Opacity + "%";
    }

    void Collect()
    {
        cfg.Level = level.Value;
        for (int i = 1; i <= 9; i++) cfg.Modes[i] = modeBoxes[i].Checked;
        cfg.DeadLines = deadLines.Checked;
        cfg.Crack = crackBox.Checked;
        cfg.Delay = (int)delay.Value;
        cfg.AutoClose = (int)autoClose.Value;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        Collect();
        cfg.Save();
        base.OnFormClosing(e);
    }

    void Launch()
    {
        Collect();
        cfg.Save();

        Hide();
        // 至少等 0.4 秒，让本窗口彻底消失后再截图
        Timer t = new Timer();
        t.Interval = Math.Max(400, cfg.Delay * 1000);
        t.Tick += delegate
        {
            t.Stop();
            t.Dispose();
            BrokenScreenForm f = new BrokenScreenForm(cfg);
            f.FormClosed += delegate { Show(); Activate(); };
            f.Show();
        };
        t.Start();
    }

    int Px(int v) { return (int)(v * s); }

    static FlowLayoutPanel Panel(FlowDirection dir)
    {
        FlowLayoutPanel p = new FlowLayoutPanel();
        p.FlowDirection = dir;
        p.WrapContents = false;
        p.AutoSize = true;
        p.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        p.Margin = new Padding(0);
        return p;
    }

    static FlowLayoutPanel Row(params Control[] cs)
    {
        FlowLayoutPanel p = Panel(FlowDirection.LeftToRight);
        p.Controls.AddRange(cs);
        return p;
    }

    Label Header(string text)
    {
        Label l = new Label();
        l.Text = text;
        l.AutoSize = true;
        l.Font = new Font(Font, FontStyle.Bold);
        l.Margin = new Padding(0, Px(12), 0, Px(4));
        return l;
    }

    Label Caption(string text)
    {
        Label l = new Label();
        l.Text = text;
        l.AutoSize = true;
        l.Margin = new Padding(0, Px(6), Px(4), 0);
        return l;
    }

    CheckBox Check(string text, bool on)
    {
        CheckBox c = new CheckBox();
        c.Text = text;
        c.Checked = on;
        c.AutoSize = true;
        c.Margin = new Padding(0, Px(2), Px(16), Px(2));
        return c;
    }

    Button SmallButton(string text)
    {
        Button b = new Button();
        b.Text = text;
        b.AutoSize = true;
        b.Margin = new Padding(0, Px(6), Px(6), 0);
        return b;
    }

    ComboBox Combo(string[] items)
    {
        ComboBox c = new ComboBox();
        c.DropDownStyle = ComboBoxStyle.DropDownList;
        c.Items.AddRange(items);
        c.Width = Px(100);
        c.Margin = new Padding(0, Px(3), Px(6), 0);
        return c;
    }

    NumericUpDown Number()
    {
        NumericUpDown n = new NumericUpDown();
        n.Minimum = 0;
        n.Maximum = 3600;
        n.Width = Px(64);
        n.Margin = new Padding(0, Px(3), Px(4), 0);
        return n;
    }
}

class DeadLine
{
    public int X, Y, W, H;
    public Color C;
    public bool Flicker;
}

class BrokenScreenForm : Form
{
    const int Opaque = unchecked((int)0xFF000000);
    const int NoiseW = 320, NoiseH = 180;

    static readonly Color[] Palette =
    {
        Color.Red, Color.Lime, Color.Blue, Color.Cyan,
        Color.Magenta, Color.Yellow, Color.White, Color.Black
    };

    static readonly Color[] TopBars =
    {
        Color.FromArgb(192, 192, 192), Color.FromArgb(192, 192, 0), Color.FromArgb(0, 192, 192),
        Color.FromArgb(0, 192, 0), Color.FromArgb(192, 0, 192), Color.FromArgb(192, 0, 0),
        Color.FromArgb(0, 0, 192)
    };

    readonly Random rnd = new Random();
    readonly GlitchSettings cfg;
    readonly List<int> enabledModes = new List<int>();
    readonly int W, H;
    readonly Bitmap shot;          // 原始桌面截图
    readonly Bitmap noise;
    readonly int[] noiseBuf = new int[NoiseW * NoiseH];
    readonly Timer timer;
    readonly List<DeadLine> lines = new List<DeadLine>();

    int[] shotPx;                  // 截图像素，按需读取
    Bitmap inverted, aberration;   // 按需生成的变体
    Bitmap crack;                  // 玻璃裂纹层，只覆盖裂纹所在区域
    Point crackPos;
    readonly Bitmap overlay;       // 素材合成的全屏图层，没有素材时为 null

    const int BreakFrame = 45;     // 开头约 1.5 秒一切正常，之后瞬间坏掉
    int frame;
    int mode;                      // 0 = 基本正常
    int modeLeft = BreakFrame;
    int modeTotal = BreakFrame;
    int seed;
    int barsTop, rollY, rollSpeed;
    readonly float intensity;      // 故障强度，由程度决定，全程不变
    int startTick;

    public BrokenScreenForm(GlitchSettings settings)
    {
        cfg = settings;
        intensity = 0.2f * cfg.Level;
        for (int i = 1; i <= 9; i++)
            if (cfg.Modes[i]) enabledModes.Add(i);

        Rectangle vs = SystemInformation.VirtualScreen;
        W = vs.Width;
        H = vs.Height;

        shot = new Bitmap(W, H, PixelFormat.Format32bppPArgb);
        using (Graphics g = Graphics.FromImage(shot))
            g.CopyFromScreen(vs.Left, vs.Top, 0, 0, vs.Size);

        overlay = Materials.BuildOverlay(cfg.Materials, W, H, rnd);
        noise = new Bitmap(NoiseW, NoiseH, PixelFormat.Format32bppPArgb);

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = vs;
        TopMost = true;
        ShowInTaskbar = false;
        DoubleBuffered = true;
        BackColor = Color.Black;
        KeyPreview = true;

        timer = new Timer();
        timer.Interval = 33;
        timer.Tick += delegate { Step(); Invalidate(); };
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Cursor.Hide();
        Activate();
        startTick = Environment.TickCount;
        timer.Start();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape) Close();
        base.OnKeyDown(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        timer.Stop();
        timer.Dispose();
        Cursor.Show();
        shot.Dispose();
        noise.Dispose();
        if (inverted != null) inverted.Dispose();
        if (aberration != null) aberration.Dispose();
        if (crack != null) crack.Dispose();
        if (overlay != null) overlay.Dispose();
        base.OnFormClosed(e);
    }

    protected override void OnPaintBackground(PaintEventArgs e) { }

    // ---------------------------------------------------------------- 逻辑

    void Step()
    {
        if (cfg.AutoClose > 0 && Environment.TickCount - startTick >= cfg.AutoClose * 1000)
        {
            Close();
            return;
        }

        frame++;
        if (frame == BreakFrame) Break();
        if (--modeLeft <= 0) PickMode();
        if (mode == 7) rollY += rollSpeed;
    }

    // 一次性生成所有固定损伤，之后不再增加
    void Break()
    {
        if (cfg.DeadLines)
            for (int i = 0; i < 3 * cfg.Level; i++) AddDeadLine();
        if (cfg.Crack) MakeCrack();
    }

    void PickMode()
    {
        seed = rnd.Next();
        // 程度越低，两次故障之间正常的时间越长
        double calm = Math.Max(0.05, 0.9 - 0.12 * cfg.Level - 0.25 * intensity);
        if (enabledModes.Count == 0 || (mode != 0 && rnd.NextDouble() < calm))
        {
            SetMode(0, Math.Max(3, rnd.Next(10, 70) * (6 - cfg.Level) / 3));
            return;
        }

        int m = enabledModes[rnd.Next(enabledModes.Count)];
        switch (m)
        {
            case 1: SetMode(m, Dur(10, 40)); break;   // 画面撕裂
            case 2: SetMode(m, Dur(5, 30)); break;    // 色差错位
            case 3:                                   // 彩条
                barsTop = rnd.Next(3) == 0 ? rnd.Next(H / 4, H * 2 / 3) : 0;
                SetMode(m, Dur(15, 60));
                break;
            case 4: SetMode(m, Dur(5, 25)); break;    // 雪花噪点
            case 5: SetMode(m, Dur(10, 45)); break;   // 彩色条纹
            case 6: SetMode(m, Dur(2, 8)); break;     // 反色闪烁
            case 7:                                   // 垂直滚屏
                rollY = 0;
                rollSpeed = Math.Max(8, H / rnd.Next(15, 50));
                SetMode(m, Dur(20, 60));
                break;
            case 8: SetMode(m, Dur(10, 40)); break;   // 马赛克块
            case 9: SetMode(m, rnd.Next(20, 40)); break; // 显像管收缩黑屏，动画需要完整时长
        }
    }

    // 故障持续帧数，程度越高持续越久
    int Dur(int min, int max)
    {
        return Math.Max(2, rnd.Next(min, max) * (2 + cfg.Level) / 5);
    }

    void SetMode(int m, int frames)
    {
        mode = m;
        modeLeft = modeTotal = frames;
    }

    void AddDeadLine()
    {
        bool vertical = rnd.NextDouble() < 0.8;

        // 偶尔出现一整束 RGB 细线
        if (rnd.NextDouble() < 0.15)
        {
            int count = rnd.Next(6, 30);
            int start = vertical ? rnd.Next(W) : rnd.Next(H);
            for (int i = 0; i < count; i++)
            {
                DeadLine b = new DeadLine();
                b.C = i % 3 == 0 ? Color.Red : i % 3 == 1 ? Color.Lime : Color.Blue;
                if (vertical) { b.X = start + i; b.W = 1; b.H = H; }
                else { b.Y = start + i; b.H = 1; b.W = W; }
                lines.Add(b);
            }
            return;
        }

        DeadLine l = new DeadLine();
        l.C = Palette[rnd.Next(Palette.Length)];
        l.Flicker = rnd.NextDouble() < 0.3;
        int thick = rnd.NextDouble() < 0.85 ? rnd.Next(1, 4) : rnd.Next(4, 20);
        if (vertical)
        {
            l.X = rnd.Next(W);
            l.W = thick;
            l.Y = rnd.NextDouble() < 0.6 ? 0 : rnd.Next(H);
            l.H = H - l.Y;
        }
        else
        {
            l.Y = rnd.Next(H);
            l.H = thick;
            l.X = rnd.NextDouble() < 0.6 ? 0 : rnd.Next(W);
            l.W = W - l.X;
        }
        lines.Add(l);
    }

    // ---------------------------------------------------------------- 绘制

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.CompositingQuality = CompositingQuality.HighSpeed;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.SmoothingMode = SmoothingMode.None;
        g.PixelOffsetMode = PixelOffsetMode.Half;

        switch (mode)
        {
            case 0: DrawCalm(g); break;
            case 1: DrawTear(g, shot); break;
            case 2: DrawAberration(g); break;
            case 3: DrawBars(g); break;
            case 4: DrawNoise(g); break;
            case 5: DrawStripes(g); break;
            case 6: Blit(g, GetInverted(), 0, 0, 0, 0, W, H); break;
            case 7: DrawRoll(g); break;
            case 8: DrawBlocks(g); break;
            case 9: DrawCollapse(g); break;
        }

        DrawPersistent(g);
    }

    void Blit(Graphics g, Image img, int dx, int dy, int sx, int sy, int w, int h)
    {
        g.DrawImage(img, new Rectangle(dx, dy, w, h), sx, sy, w, h, GraphicsUnit.Pixel);
    }

    void Fill(Graphics g, Color c, int x, int y, int w, int h)
    {
        using (SolidBrush b = new SolidBrush(c)) g.FillRectangle(b, x, y, w, h);
    }

    void DrawCalm(Graphics g)
    {
        Blit(g, shot, 0, 0, 0, 0, W, H);

        // 偶发的小抖动
        if (frame > 45 && rnd.NextDouble() < 0.03 + 0.2 * intensity)
        {
            int y = rnd.Next(H), bh = Math.Min(rnd.Next(2, 40), H - y);
            int dx = rnd.Next(-60, 61);
            Blit(g, shot, dx, y, 0, y, W, bh);
        }
        if (frame > 45 && rnd.NextDouble() < 0.05 * intensity)
            Fill(g, Color.FromArgb(rnd.Next(30, 120), 0, 0, 0), 0, 0, W, H);
    }

    void DrawTear(Graphics g, Bitmap src)
    {
        Blit(g, src, 0, 0, 0, 0, W, H);
        Random r = new Random(seed + frame / 2);
        int maxShift = (int)(W * (0.05 + 0.25 * intensity));
        int y = 0;
        while (y < H)
        {
            int bh = Math.Min(r.Next(3, 90), H - y);
            if (r.NextDouble() < 0.45)
            {
                int dx = r.Next(-maxShift, maxShift + 1);
                Blit(g, src, dx, y, 0, y, W, bh);
                if (dx > 0) Blit(g, src, dx - W, y, 0, y, W, bh);
                else if (dx < 0) Blit(g, src, dx + W, y, 0, y, W, bh);

                if (r.NextDouble() < 0.3)
                {
                    Color c = Palette[r.Next(6)];
                    Fill(g, Color.FromArgb(80, c), 0, y, W, bh);
                }
            }
            y += bh;
        }
    }

    void DrawAberration(Graphics g)
    {
        Bitmap ab = GetAberration();
        if (rnd.NextDouble() < 0.5) DrawTear(g, ab);
        else Blit(g, ab, rnd.Next(-6, 7), 0, 0, 0, W, H);
    }

    void DrawBars(Graphics g)
    {
        if (barsTop > 0) Blit(g, shot, 0, 0, 0, 0, W, H);

        int jx = rnd.Next(-4, 5);
        int jy = rnd.NextDouble() < 0.1 ? rnd.Next(-H / 10, H / 10) : 0;
        int top = barsTop + jy;
        int height = H - barsTop;
        int h1 = height * 67 / 100, h2 = height * 8 / 100, h3 = height - h1 - h2;
        float bw = W / 7f;

        Fill(g, Color.Black, 0, barsTop, W, height);
        for (int i = 0; i < 7; i++)
        {
            using (SolidBrush b = new SolidBrush(TopBars[i]))
                g.FillRectangle(b, jx + i * bw, top, bw + 1, h1);

            Color mid = i % 2 == 1 ? Color.FromArgb(19, 19, 19) : TopBars[6 - i];
            using (SolidBrush b = new SolidBrush(mid))
                g.FillRectangle(b, jx + i * bw, top + h1, bw + 1, h2);
        }

        Color[] bottom =
        {
            Color.FromArgb(0, 33, 76), Color.White, Color.FromArgb(50, 0, 106),
            Color.FromArgb(19, 19, 19), Color.Black, Color.FromArgb(19, 19, 19), Color.FromArgb(38, 38, 38)
        };
        for (int i = 0; i < 7; i++)
            using (SolidBrush b = new SolidBrush(bottom[i]))
                g.FillRectangle(b, jx + i * bw, top + h1 + h2, bw + 1, h3);

        // 信号不稳的横向干扰线
        int n = rnd.Next(0, 6);
        for (int i = 0; i < n; i++)
        {
            int y = rnd.Next(barsTop, H);
            Fill(g, Color.FromArgb(rnd.Next(60, 200), 255, 255, 255), 0, y, W, rnd.Next(1, 6));
        }
    }

    void DrawNoise(Graphics g)
    {
        bool color = rnd.NextDouble() < 0.25;
        for (int i = 0; i < noiseBuf.Length; i++)
        {
            if (color)
                noiseBuf[i] = Opaque | rnd.Next(0x1000000);
            else
            {
                int v = rnd.Next(256);
                noiseBuf[i] = Opaque | (v << 16) | (v << 8) | v;
            }
        }
        WritePixels(noise, noiseBuf);
        g.DrawImage(noise, new Rectangle(0, 0, W, H));

        // 偶尔透出一点原画面
        if (rnd.NextDouble() < 0.3)
        {
            int y = rnd.Next(H), bh = Math.Min(rnd.Next(20, H / 3), H - y);
            Blit(g, shot, rnd.Next(-40, 41), y, 0, y, W, bh);
        }
    }

    void DrawStripes(Graphics g)
    {
        Blit(g, shot, 0, 0, 0, 0, W, H);
        Random r = new Random(seed + frame / 3);

        // 一大片重复的彩色竖条纹（类似排线/驱动损坏）
        if (r.NextDouble() < 0.6)
        {
            int tw = r.Next(20, 120);
            using (Bitmap tex = new Bitmap(tw, 1, PixelFormat.Format32bppPArgb))
            {
                for (int x = 0; x < tw; x++)
                    tex.SetPixel(x, 0, Palette[r.Next(Palette.Length)]);
                using (TextureBrush tb = new TextureBrush(tex))
                {
                    int x0 = r.Next(W), rw = r.Next(W / 10, W / 2);
                    int y0 = r.NextDouble() < 0.5 ? 0 : r.Next(H);
                    g.FillRectangle(tb, x0, y0, rw, H - y0);
                }
            }
        }

        int n = r.Next(40, 250);
        for (int i = 0; i < n; i++)
        {
            Color c = Palette[r.Next(Palette.Length)];
            if (r.NextDouble() < 0.85)
            {
                int x = r.Next(W), y = r.NextDouble() < 0.6 ? 0 : r.Next(H);
                Fill(g, c, x, y, r.Next(1, 8), H - y);
            }
            else
            {
                int y = r.Next(H), x = r.NextDouble() < 0.6 ? 0 : r.Next(W);
                Fill(g, c, x, y, W - x, r.Next(1, 6));
            }
        }
    }

    void DrawRoll(Graphics g)
    {
        int gap = Math.Max(30, H / 20);
        int period = H + gap;
        int y0 = rollY % period;
        Blit(g, shot, 0, y0, 0, 0, W, H);
        Blit(g, shot, 0, y0 - period, 0, 0, W, H);
        Fill(g, Color.Black, 0, y0 - gap, W, gap);
        Fill(g, Color.FromArgb(90, 90, 90), 0, y0 - gap / 2, W, 2);
    }

    void DrawBlocks(Graphics g)
    {
        Blit(g, shot, 0, 0, 0, 0, W, H);
        Random r = new Random(seed + frame / 2);
        int bs = 16 * r.Next(1, 5);
        int n = r.Next(20, 150);
        for (int i = 0; i < n; i++)
        {
            int bw = bs * r.Next(1, 12), bh = bs * r.Next(1, 4);
            int dx = r.Next(W / bs) * bs, dy = r.Next(H / bs) * bs;
            double p = r.NextDouble();
            if (p < 0.2)
                Fill(g, r.Next(2) == 0 ? Color.FromArgb(0, 200, 0) : Color.FromArgb(220, 0, 220), dx, dy, bw, bh);
            else if (p < 0.3)
                Fill(g, Color.FromArgb(r.Next(256), r.Next(256), r.Next(256)), dx, dy, bw, bh);
            else
            {
                int sx = Math.Min(r.Next(W / bs) * bs, Math.Max(0, W - bw));
                int sy = Math.Min(r.Next(H / bs) * bs, Math.Max(0, H - bh));
                Blit(g, shot, dx, dy, sx, sy, bw, bh);
            }
        }
    }

    void DrawCollapse(Graphics g)
    {
        Fill(g, Color.Black, 0, 0, W, H);
        float t = 1f - (float)modeLeft / modeTotal;
        if (t < 0.5f)
        {
            // 画面被压成一条横线
            float k = 1f - t / 0.5f;
            int h = Math.Max(2, (int)(H * k * k));
            g.DrawImage(shot, new Rectangle(0, (H - h) / 2, W, h), 0, 0, W, H, GraphicsUnit.Pixel);
        }
        else if (t < 0.75f)
        {
            // 横线缩成一个亮点
            float k = 1f - (t - 0.5f) / 0.25f;
            int w = Math.Max(4, (int)(W * k * k));
            Fill(g, Color.White, (W - w) / 2, H / 2 - 1, w, 3);
        }
        else if (rnd.NextDouble() < 0.15)
        {
            // 黑屏中偶尔闪回
            Blit(g, shot, 0, 0, 0, 0, W, H);
            Fill(g, Color.FromArgb(170, 0, 0, 0), 0, 0, W, H);
        }
    }

    void DrawPersistent(Graphics g)
    {
        if (frame < BreakFrame) return;
        foreach (DeadLine l in lines)
        {
            if (l.Flicker && rnd.Next(3) == 0) continue;
            Fill(g, l.C, l.X, l.Y, l.W, l.H);
        }
        if (crack != null)
            g.DrawImage(crack, new Rectangle(crackPos, crack.Size), 0, 0, crack.Width, crack.Height, GraphicsUnit.Pixel);
        if (overlay != null)
            g.DrawImage(overlay, new Rectangle(0, 0, W, H), 0, 0, W, H, GraphicsUnit.Pixel);
    }

    // ---------------------------------------------------------------- 裂纹

    void MakeCrack()
    {
        int R = (int)(Math.Min(W, H) * 0.45);
        int cx = rnd.Next(W / 5, W * 4 / 5), cy = rnd.Next(H / 5, H * 4 / 5);
        crackPos = new Point(cx - R, cy - R);
        Bitmap bmp = new Bitmap(2 * R, 2 * R, PixelFormat.Format32bppPArgb);

        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            // 液晶漏液的黑斑
            for (int i = 0; i < 7; i++)
            {
                float br = rnd.Next(R / 14, R / 5) * (i == 0 ? 1f : 0.5f);
                float ox = i == 0 ? 0 : rnd.Next(-R / 5, R / 5);
                float oy = i == 0 ? 0 : rnd.Next(-R / 5, R / 5);
                g.FillPolygon(Brushes.Black, Blob(R + ox, R + oy, br));
            }

            // 放射状裂纹
            int rays = rnd.Next(9, 16);
            const int segs = 14;
            List<PointF[]> all = new List<PointF[]>();
            for (int i = 0; i < rays; i++)
            {
                double ang = i * 2 * Math.PI / rays + (rnd.NextDouble() - 0.5) * 0.4;
                double len = R * (0.45 + 0.55 * rnd.NextDouble());
                PointF[] pts = new PointF[segs + 1];
                for (int k = 0; k <= segs; k++)
                {
                    ang += (rnd.NextDouble() - 0.5) * 0.25;
                    double d = len * k / segs;
                    pts[k] = new PointF(R + (float)(Math.Cos(ang) * d), R + (float)(Math.Sin(ang) * d));
                }
                all.Add(pts);
                DrawCrackLine(g, pts, rnd.NextDouble() < 0.3 ? 2f : 1f);

                // 分叉
                int branches = rnd.Next(0, 3);
                for (int b = 0; b < branches; b++)
                {
                    int k0 = rnd.Next(3, segs - 2);
                    double bang = ang + (rnd.Next(2) == 0 ? 1 : -1) * (0.4 + rnd.NextDouble() * 0.6);
                    PointF[] bp = new PointF[6];
                    bp[0] = pts[k0];
                    for (int k = 1; k < bp.Length; k++)
                    {
                        bang += (rnd.NextDouble() - 0.5) * 0.4;
                        double d = len / segs * 0.9;
                        bp[k] = new PointF(bp[k - 1].X + (float)(Math.Cos(bang) * d), bp[k - 1].Y + (float)(Math.Sin(bang) * d));
                    }
                    DrawCrackLine(g, bp, 1f);
                }
            }

            // 蛛网状的环形裂纹
            for (int ring = 1; ring <= 3; ring++)
            {
                int k = ring * 3 + rnd.Next(-1, 2);
                for (int i = 0; i < rays; i++)
                {
                    if (rnd.NextDouble() > 0.65) continue;
                    PointF a = all[i][k], b = all[(i + 1) % rays][k];
                    PointF m = new PointF((a.X + b.X) / 2 + rnd.Next(-8, 9), (a.Y + b.Y) / 2 + rnd.Next(-8, 9));
                    DrawCrackLine(g, new[] { a, m, b }, 1f);
                }
            }
        }
        crack = bmp;

        // 从撞击点延伸出去的彩色坏线
        int nv = rnd.Next(3, 9);
        for (int i = 0; i < nv; i++)
        {
            DeadLine l = new DeadLine();
            l.X = cx + rnd.Next(-R / 3, R / 3);
            l.W = rnd.Next(1, 4);
            l.H = H;
            l.C = Palette[rnd.Next(7)];
            l.Flicker = rnd.NextDouble() < 0.2;
            lines.Add(l);
        }
        int nh = rnd.Next(1, 4);
        for (int i = 0; i < nh; i++)
        {
            DeadLine l = new DeadLine();
            l.Y = cy + rnd.Next(-R / 4, R / 4);
            l.H = rnd.Next(1, 3);
            l.W = W;
            l.C = Palette[rnd.Next(7)];
            lines.Add(l);
        }
    }

    PointF[] Blob(float x, float y, float r)
    {
        int n = rnd.Next(12, 20);
        PointF[] p = new PointF[n];
        for (int i = 0; i < n; i++)
        {
            double a = i * 2 * Math.PI / n;
            double d = r * (0.5 + 0.7 * rnd.NextDouble());
            p[i] = new PointF(x + (float)(Math.Cos(a) * d), y + (float)(Math.Sin(a) * d));
        }
        return p;
    }

    static void DrawCrackLine(Graphics g, PointF[] pts, float w)
    {
        using (Pen dark = new Pen(Color.FromArgb(170, 0, 0, 0), w + 2))
            g.DrawLines(dark, pts);
        using (Pen light = new Pen(Color.FromArgb(215, 235, 242, 255), w))
            g.DrawLines(light, pts);
    }

    // ---------------------------------------------------------------- 像素处理

    int[] GetShotPixels()
    {
        if (shotPx == null) shotPx = Pixels.Read(shot, PixelFormat.Format32bppPArgb);
        return shotPx;
    }

    static void WritePixels(Bitmap b, int[] px) { Pixels.Write(b, px); }

    Bitmap GetInverted()
    {
        if (inverted == null)
        {
            int[] src = GetShotPixels();
            int[] dst = new int[src.Length];
            for (int i = 0; i < src.Length; i++) dst[i] = Opaque | (~src[i] & 0xFFFFFF);
            inverted = new Bitmap(W, H, PixelFormat.Format32bppPArgb);
            WritePixels(inverted, dst);
        }
        return inverted;
    }

    Bitmap GetAberration()
    {
        if (aberration == null)
        {
            int[] src = GetShotPixels();
            int[] dst = new int[src.Length];
            int off = Math.Max(6, W / 200);
            for (int y = 0; y < H; y++)
            {
                int row = y * W;
                for (int x = 0; x < W; x++)
                {
                    int xr = Math.Min(W - 1, x + off), xb = Math.Max(0, x - off);
                    dst[row + x] = Opaque | (src[row + xr] & 0xFF0000) | (src[row + x] & 0xFF00) | (src[row + xb] & 0xFF);
                }
            }
            aberration = new Bitmap(W, H, PixelFormat.Format32bppPArgb);
            WritePixels(aberration, dst);
        }
        return aberration;
    }
}
