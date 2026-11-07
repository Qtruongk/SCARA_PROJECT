using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using RobotControl.Models;
using RobotControl.Services;

namespace RobotControl
{
    public class MainForm : Form
    {
        // ── Services ──────────────────────────────────────────────────────
        private readonly RobotService _robot = new RobotService();

        // ── Connection ────────────────────────────────────────────────────
        private TextBox _txtIP, _txtPort;
        private Button  _btnConnect, _btnDisconnect;

        // ── Status ────────────────────────────────────────────────────────
        private Label _lblConnDot,  _lblConnText;
        private Label _lblServoDot, _lblServoText;
        private Label _lblWatchDot, _lblWatchText;

        // ── Robot control ─────────────────────────────────────────────────
        private Button _btnServoOn, _btnServoOff, _btnWatchdog, _btnStop;

        // ── Mode selection (Cartesian vs Joint) ───────────────────────────
        private Button _btnModeCart, _btnModeJoint;
        private bool   _isCartesianMode = true;
        private Panel  _pnlCartesian, _pnlJoint;

        // ── Move inputs ───────────────────────────────────────────────────
        private TextBox _txtX, _txtY, _txtZ, _txtC;
        private TextBox _txtJ1, _txtJ2, _txtJ3, _txtJ4;
        private Button  _btnReadCurrent;
        private TrackBar _tbSpeed;
        private Label    _lblSpeed;
        private Button   _btnMove;

        // ── Feedback ──────────────────────────────────────────────────────
        private Label _fbX, _fbY, _fbZ, _fbC;
        private Label _fbJ1, _fbJ2, _fbJ3, _fbJ4;

        // ── Log ───────────────────────────────────────────────────────────
        private RichTextBox _log;

        // ── Timer ─────────────────────────────────────────────────────────
        private System.Windows.Forms.Timer _feedbackTimer;

        // ── Plotter ───────────────────────────────────────────────────────
        private TextBox  _txtPlotXc, _txtPlotYc;
        private TextBox  _txtSquareSize, _txtCircleDia, _txtCircleSegs, _txtTriHeight;
        private TextBox  _txtPlotZDraw, _txtPlotZLift;
        private TrackBar _tbPlotSpeed;
        private Label    _lblPlotSpeed;
        private CheckBox _chkDryRun, _chkPauseOnPenDown;
        private Button   _btnPlotSetCenter, _btnPlotSetZDraw, _btnPlotSetZLift;
        private Button   _btnPlotPenDown, _btnPlotPenUp;
        private Button   _btnPlotZInc1, _btnPlotZDec1, _btnPlotZInc05, _btnPlotZDec05;
        private Button   _btnDrawSquare, _btnDrawTriangle, _btnDrawX, _btnDrawCircle;
        private Button   _btnAbortDraw, _btnContinueDraw;
        private bool     _isDrawing;
        private CancellationTokenSource _drawCts;
        private readonly ManualResetEventSlim _continueDrawEvent = new ManualResetEventSlim(false);

        // ── Sequencer (SEQ) ───────────────────────────────────────────────
        private List<TeachPoint> _points = new List<TeachPoint>();
        private DataGridView  _dgvPoints;
        private Button _btnSeqAddPoint, _btnSeqDeletePoint, _btnSeqMoveUp, _btnSeqMoveDown;
        private Button _btnSeqTeach, _btnSeqSave, _btnSeqLoad;
        private Button _btnSeqRun, _btnSeqStop;
        private CheckBox _chkSeqLoop;
        private TextBox  _txtSeqZLift;
        private Label    _lblSeqStatus;
        private bool     _isRunningSeq;
        private CancellationTokenSource _seqCts;

        // ── Palette (Light Theme) ─────────────────────────────────────────
        static readonly Color BgDark   = Color.FromArgb(245, 246, 250);   // body background
        static readonly Color BgPanel  = Color.FromArgb(255, 255, 255);   // sidebar / card white
        static readonly Color BgCard   = Color.FromArgb(237, 241, 247);   // inner card gray
        static readonly Color Blue     = Color.FromArgb(37,  99,  235);   // primary accent
        static readonly Color Red      = Color.FromArgb(220, 38,  38);    // danger
        static readonly Color Green    = Color.FromArgb(22,  163, 74);    // success
        static readonly Color Orange   = Color.FromArgb(234, 88,  12);    // warning
        static readonly Color TextMain = Color.FromArgb(15,  23,  42);    // primary text dark
        static readonly Color TextMute = Color.FromArgb(100, 116, 139);   // secondary text

        // ── Sidebar nav state ─────────────────────────────────────────────
        private Panel   _pnlTab1, _pnlTab2, _pnlTab3;
        private Button  _navBtn1, _navBtn2, _navBtn3;
        private int     _activeTab = 1;

        // ── Sidebar colors ────────────────────────────────────────────────
        static readonly Color SidebarBg     = Color.FromArgb(30,  58,  138);
        static readonly Color SidebarActive = Color.FromArgb(59,  130, 246);

        public MainForm()
        {
            _robot.OnLog          += AppendLog;
            _robot.OnStateChanged += () => { if (InvokeRequired) Invoke((Action)UpdateStatus); else UpdateStatus(); };
            Build();
            _feedbackTimer = new System.Windows.Forms.Timer { Interval = 500 };
            _feedbackTimer.Tick += (s, e) => RefreshFeedback();
        }

        void Build()
        {
            Text            = "SCARA Plotter — THL300";
            MinimumSize     = new Size(1280, 720);
            BackColor       = BgDark;
            ForeColor       = TextMain;
            Font            = new Font("Segoe UI", 9.5f);
            StartPosition   = FormStartPosition.CenterScreen;
            MaximizeBox     = true;
            WindowState     = FormWindowState.Maximized;
            FormBorderStyle = FormBorderStyle.Sizable;
            FormClosing    += (s, e) => { _feedbackTimer.Stop(); _robot.Disconnect(); };

            // ── Root: full-form TableLayoutPanel — 2 rows
            // Row 0 (44px): Title bar
            // Row 1 (fill): Work area  (sidebar | content | right panel)
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, BackColor = BgDark,
                ColumnCount = 1, RowCount = 2, Padding = new Padding(0)
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44f));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            // ── Title bar ──────────────────────────────────────────────────
            var titleBar = new Panel { Dock = DockStyle.Fill, BackColor = SidebarBg, Padding = new Padding(0) };
            var lTitle   = Lbl("SCARA PLOTTER  —  TOSHIBA THL300 + TSL3000",
                               new Font("Segoe UI", 11f, FontStyle.Bold), Color.White);
            lTitle.Location = new Point(88, 11);
            titleBar.Controls.Add(lTitle);
            root.Controls.Add(titleBar, 0, 0);

            // ── Work area: 3 columns ───────────────────────────────────────
            var work = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, BackColor = BgDark,
                ColumnCount = 3, RowCount = 1, Margin = new Padding(0)
            };
            work.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86f));   // Sidebar (86px)
            work.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));   // Center Area (Tabs + Big Log)
            work.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 330f));  // Right panel (always visible, 330px rộng rãi)
            root.Controls.Add(work, 0, 1);

            // ── Col 0: Sidebar ─────────────────────────────────────────────
            work.Controls.Add(BuildSidebar(), 0, 0);

            // ── Col 1: Center Area (Top: Tab Content, Bottom: Large Activity Log)
            var centerGrid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, BackColor = BgDark,
                ColumnCount = 1, RowCount = 2, Margin = new Padding(0),
                Padding = new Padding(8, 8, 8, 8)
            };
            centerGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            centerGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));   // Row 0: Vùng 3 Tab chức năng (cân đối bằng nhau)
            centerGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 190f));  // Row 1: Khung to chứa Activity Log trải dài

            var tabContainer = new Panel { Dock = DockStyle.Fill, BackColor = BgDark, Margin = new Padding(0, 0, 0, 6) };

            _pnlTab1 = new Panel { Dock = DockStyle.Fill, BackColor = BgDark, Visible = true,  Padding = new Padding(0) };
            _pnlTab2 = new Panel { Dock = DockStyle.Fill, BackColor = BgDark, Visible = false, Padding = new Padding(0) };
            _pnlTab3 = new Panel { Dock = DockStyle.Fill, BackColor = BgDark, Visible = false, Padding = new Padding(0) };

            _pnlTab1.Controls.Add(BuildMiddle());
            _pnlTab2.Controls.Add(BuildPlotterColumn());
            _pnlTab3.Controls.Add(BuildSequencerPanel());

            tabContainer.Controls.Add(_pnlTab3);
            tabContainer.Controls.Add(_pnlTab2);
            tabContainer.Controls.Add(_pnlTab1);

            centerGrid.Controls.Add(tabContainer, 0, 0);
            centerGrid.Controls.Add(BuildBottomLog(), 0, 1);

            work.Controls.Add(centerGrid, 1, 0);

            // ── Col 2: Right panel (always visible) ───────────────────────
            work.Controls.Add(BuildRightPanel(), 2, 0);

            Controls.Add(root);

            SwitchTab(1);
            UpdateStatus();
            AppendLog("[INFO] Hệ thống khởi tạo thành công. Sẵn sàng kết nối.");
        }

        // =================================================================
        //  SIDEBAR — 86px wide, navy blue, 3 nav buttons
        // =================================================================
        Panel BuildSidebar()
        {
            var sb = new Panel
            {
                Dock = DockStyle.Fill, BackColor = SidebarBg,
                Padding = new Padding(0)
            };

            // Nav buttons stack - 3 nút đều nhau, nằm ngay ngắn từ trên xuống
            _navBtn1 = NavBtn("🎮", "Điều khiển");
            _navBtn2 = NavBtn("📐", "Vẽ hình");
            _navBtn3 = NavBtn("📋", "Dây chuyền");

            _navBtn1.Click += (s, e) => SwitchTab(1);
            _navBtn2.Click += (s, e) => SwitchTab(2);
            _navBtn3.Click += (s, e) => SwitchTab(3);

            var navStack = new TableLayoutPanel
            {
                Dock = DockStyle.Top, BackColor = SidebarBg,
                ColumnCount = 1, RowCount = 3, Margin = new Padding(0),
                Height = 240
            };
            navStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 78f));
            navStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 78f));
            navStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 78f));

            navStack.Controls.Add(_navBtn1, 0, 0);
            navStack.Controls.Add(_navBtn2, 0, 1);
            navStack.Controls.Add(_navBtn3, 0, 2);
            sb.Controls.Add(navStack);

            return sb;
        }

        Button NavBtn(string icon, string label)
        {
            var btn = new Button
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent, ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Text = icon + "\n" + label,
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand, Margin = new Padding(4, 4, 4, 4)
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(49, 80, 150);
            return btn;
        }

        void SwitchTab(int tab)
        {
            _activeTab = tab;
            _pnlTab1.Visible = (tab == 1);
            _pnlTab2.Visible = (tab == 2);
            _pnlTab3.Visible = (tab == 3);

            void StyleNav(Button btn, bool active)
            {
                btn.BackColor = active ? SidebarActive : Color.Transparent;
            }
            StyleNav(_navBtn1, tab == 1);
            StyleNav(_navBtn2, tab == 2);
            StyleNav(_navBtn3, tab == 3);
        }

        // =================================================================
        //  RIGHT PANEL — 310px wide, always visible on all tabs
        //  Contains: Connection, Status, Control, STOP, Feedback, Target Move
        // =================================================================
        Panel BuildRightPanel()
        {
            var outer = new Panel
            {
                Dock = DockStyle.Fill, BackColor = BgDark, Padding = new Padding(4, 4, 4, 4),
                AutoScroll = true
            };

            var stack = new TableLayoutPanel
            {
                Dock = DockStyle.Top, BackColor = BgDark,
                ColumnCount = 1, RowCount = 6, AutoSize = true,
                Margin = new Padding(0)
            };
            stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 122f)); // Row 0: Connection
            stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 86f));  // Row 1: Status
            stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 100f)); // Row 2: Servo / Watchdog
            stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 46f));  // Row 3: STOP MOTION
            stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 92f));  // Row 4: Live Feedback
            stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 246f)); // Row 5: Move to Target (Di chuyển tới tọa độ đích)

            stack.Controls.Add(BuildConnectionCard(), 0, 0);
            stack.Controls.Add(BuildStatusCard(),     0, 1);
            stack.Controls.Add(BuildControlCard(),    0, 2);
            stack.Controls.Add(BuildStopCard(),       0, 3);
            stack.Controls.Add(BuildFeedbackCard(),   0, 4);
            stack.Controls.Add(BuildTargetMoveCard(), 0, 5);

            outer.Controls.Add(stack);
            return outer;
        }

        // =================================================================
        //  FEEDBACK CARD — Live robot coordinates (in right panel)
        // =================================================================
        Panel BuildFeedbackCard()
        {
            var card = new Panel { Dock = DockStyle.Fill, BackColor = BgCard, Margin = new Padding(0, 0, 0, 4), Padding = new Padding(6) };

            var lFb = Lbl("TỌA ĐỘ HIỆN TẠI (FEEDBACK)", new Font("Segoe UI", 7.5f, FontStyle.Bold), TextMute);
            lFb.Location = new Point(10, 4);
            card.Controls.Add(lFb);

            _fbX = FbLbl("X : —"); _fbY = FbLbl("Y : —");
            _fbZ = FbLbl("Z : —"); _fbC = FbLbl("C : —");
            _fbJ1 = FbLbl("J1: —"); _fbJ2 = FbLbl("J2: —");
            _fbJ3 = FbLbl("J3: —"); _fbJ4 = FbLbl("J4: —");

            var gridFb = new TableLayoutPanel
            {
                Location = new Point(10, 20),
                Width = 296, Height = 64,
                ColumnCount = 2, RowCount = 4,
                BackColor = BgCard, Margin = new Padding(0)
            };
            for (int i = 0; i < 2; i++) gridFb.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            for (int i = 0; i < 4; i++) gridFb.RowStyles.Add(new RowStyle(SizeType.Percent, 25f));

            gridFb.Controls.Add(_fbX,  0, 0); gridFb.Controls.Add(_fbY,  1, 0);
            gridFb.Controls.Add(_fbZ,  0, 1); gridFb.Controls.Add(_fbC,  1, 1);
            gridFb.Controls.Add(_fbJ1, 0, 2); gridFb.Controls.Add(_fbJ2, 1, 2);
            gridFb.Controls.Add(_fbJ3, 0, 3); gridFb.Controls.Add(_fbJ4, 1, 3);

            card.Controls.Add(gridFb);
            return card;
        }

        Panel BuildConnectionCard()
        {
            var card = Card("KẾT NỐI (ETHERNET)", 300, 118);
            card.Dock = DockStyle.Fill;
            card.Margin = new Padding(0, 0, 0, 4);

            var lIP = Lbl("IP Robot", new Font("Segoe UI", 8f), TextMute);
            lIP.Location = new Point(10, 24); card.Controls.Add(lIP);
            _txtIP = Input("192.168.0.124", 178);
            _txtIP.Location = new Point(10, 42); card.Controls.Add(_txtIP);

            var lPort = Lbl("Port", new Font("Segoe UI", 8f), TextMute);
            lPort.Location = new Point(198, 24); card.Controls.Add(lPort);
            _txtPort = Input("1000", 108);
            _txtPort.Location = new Point(198, 42); card.Controls.Add(_txtPort);

            _btnConnect    = Btn("Connect",    Blue,    144, 32);
            _btnDisconnect = Btn("Disconnect", BgCard,  144, 32);
            _btnDisconnect.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
            _btnDisconnect.ForeColor = TextMain;
            _btnConnect.Location    = new Point(10, 76);
            _btnDisconnect.Location = new Point(162, 76);
            card.Controls.Add(_btnConnect);
            card.Controls.Add(_btnDisconnect);

            _btnConnect.Click    += (s, e) => OnConnect();
            _btnDisconnect.Click += (s, e) => { _robot.Disconnect(); _feedbackTimer.Stop(); };
            return card;
        }

        Panel BuildStatusCard()
        {
            var card = Card("TRẠNG THÁI HỆ THỐNG", 300, 82);
            card.Dock = DockStyle.Fill;
            card.Margin = new Padding(0, 0, 0, 4);
            (_lblConnDot,  _lblConnText)  = StatusRow(card, "Disconnected", 24);
            (_lblServoDot, _lblServoText) = StatusRow(card, "Servo OFF",    44);
            (_lblWatchDot, _lblWatchText) = StatusRow(card, "Watchdog OFF", 64);
            return card;
        }

        Panel BuildControlCard()
        {
            var card = Card("ĐIỀU KHIỂN NGUỒN", 300, 96);
            card.Dock = DockStyle.Fill;
            card.Margin = new Padding(0, 0, 0, 4);

            _btnServoOn  = Btn("Servo ON",  Green,  144, 30);
            _btnServoOff = Btn("Servo OFF", Orange, 144, 30);
            _btnServoOn.Location  = new Point(10,  24);
            _btnServoOff.Location = new Point(162, 24);
            card.Controls.Add(_btnServoOn);
            card.Controls.Add(_btnServoOff);

            _btnWatchdog = Btn("Start Watchdog", Blue, 296, 30);
            _btnWatchdog.Location = new Point(10, 58);
            card.Controls.Add(_btnWatchdog);

            _btnServoOn.Click  += (s, e) => _robot.ServoOn();
            _btnServoOff.Click += (s, e) => _robot.ServoOff();
            _btnWatchdog.Click += (s, e) => _robot.StartWatchdog();
            return card;
        }

        Panel BuildStopCard()
        {
            var card = new Panel { Dock = DockStyle.Fill, BackColor = BgDark, Padding = new Padding(0), Margin = new Padding(0, 0, 0, 4) };
            _btnStop = Btn("■  STOP MOTION", Red, 300, 42);
            _btnStop.Dock = DockStyle.Fill;
            _btnStop.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
            _btnStop.Click += (s, e) => _robot.StopMotion();
            card.Controls.Add(_btnStop);
            return card;
        }

        // =================================================================
        //  TARGET MOVE CARD — Di chuyển tới tọa độ đích (Always visible in right panel)
        // =================================================================
        Panel BuildTargetMoveCard()
        {
            var card = Card("DI CHUYỂN TỚI TỌA ĐỘ", 300, 240);
            card.Dock = DockStyle.Fill;
            card.Margin = new Padding(0);

            // 1. Chuyển Mode Cartesian / Joint
            _btnModeCart  = Btn("🌐 Cartesian", Blue, 144, 25);
            _btnModeJoint = Btn("🦾 Joint", BgPanel, 144, 25);
            _btnModeCart.Font = _btnModeJoint.Font = new Font("Segoe UI", 8f, FontStyle.Bold);
            _btnModeCart.Location  = new Point(10, 24);
            _btnModeJoint.Location = new Point(162, 24);
            _btnModeJoint.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);

            _btnModeCart.Click  += (s, e) => SwitchMode(true);
            _btnModeJoint.Click += (s, e) => SwitchMode(false);

            card.Controls.Add(_btnModeCart);
            card.Controls.Add(_btnModeJoint);

            // 2. Vùng nhập liệu tọa độ (Cartesian & Joint)
            var pnlInputs = new Panel { Location = new Point(10, 53), Width = 296, Height = 58, BackColor = Color.Transparent };

            _pnlCartesian = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
            _pnlJoint     = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Visible = false };

            // Cartesian: X, Y, Z, C
            var lX = Lbl("X", new Font("Segoe UI", 8f, FontStyle.Bold), TextMute); lX.Location = new Point(2, 4); _pnlCartesian.Controls.Add(lX);
            _txtX = Input("0.0", 108); _txtX.Location = new Point(22, 2); _pnlCartesian.Controls.Add(_txtX);

            var lY = Lbl("Y", new Font("Segoe UI", 8f, FontStyle.Bold), TextMute); lY.Location = new Point(152, 4); _pnlCartesian.Controls.Add(lY);
            _txtY = Input("0.0", 108); _txtY.Location = new Point(172, 2); _pnlCartesian.Controls.Add(_txtY);

            var lZ = Lbl("Z", new Font("Segoe UI", 8f, FontStyle.Bold), TextMute); lZ.Location = new Point(2, 32); _pnlCartesian.Controls.Add(lZ);
            _txtZ = Input("0.0", 108); _txtZ.Location = new Point(22, 30); _pnlCartesian.Controls.Add(_txtZ);

            var lC = Lbl("C", new Font("Segoe UI", 8f, FontStyle.Bold), TextMute); lC.Location = new Point(152, 32); _pnlCartesian.Controls.Add(lC);
            _txtC = Input("0.0", 108); _txtC.Location = new Point(172, 30); _pnlCartesian.Controls.Add(_txtC);

            // Joint: J1, J2, J3, J4
            var lJ1 = Lbl("J1", new Font("Segoe UI", 8f, FontStyle.Bold), TextMute); lJ1.Location = new Point(2, 4); _pnlJoint.Controls.Add(lJ1);
            _txtJ1 = Input("0.0", 104); _txtJ1.Location = new Point(26, 2); _pnlJoint.Controls.Add(_txtJ1);

            var lJ2 = Lbl("J2", new Font("Segoe UI", 8f, FontStyle.Bold), TextMute); lJ2.Location = new Point(148, 4); _pnlJoint.Controls.Add(lJ2);
            _txtJ2 = Input("0.0", 104); _txtJ2.Location = new Point(172, 2); _pnlJoint.Controls.Add(_txtJ2);

            var lJ3 = Lbl("J3", new Font("Segoe UI", 8f, FontStyle.Bold), TextMute); lJ3.Location = new Point(2, 32); _pnlJoint.Controls.Add(lJ3);
            _txtJ3 = Input("0.0", 104); _txtJ3.Location = new Point(26, 30); _pnlJoint.Controls.Add(_txtJ3);

            var lJ4 = Lbl("J4", new Font("Segoe UI", 8f, FontStyle.Bold), TextMute); lJ4.Location = new Point(148, 32); _pnlJoint.Controls.Add(lJ4);
            _txtJ4 = Input("0.0", 104); _txtJ4.Location = new Point(172, 30); _pnlJoint.Controls.Add(_txtJ4);

            pnlInputs.Controls.Add(_pnlCartesian);
            pnlInputs.Controls.Add(_pnlJoint);
            card.Controls.Add(pnlInputs);

            // 3. Nút nạp tọa độ thực hiện tại
            _btnReadCurrent = Btn("⟳  Nạp tọa độ hiện tại vào ô", Green, 296, 25);
            _btnReadCurrent.Font = new Font("Segoe UI", 8f, FontStyle.Bold);
            _btnReadCurrent.Location = new Point(10, 116);
            _btnReadCurrent.Click += (s, e) => CopyFeedbackToInputs();
            card.Controls.Add(_btnReadCurrent);

            // 4. Thanh trượt tốc độ
            var lSpd = Lbl("Tốc độ:", new Font("Segoe UI", 8f, FontStyle.Bold), TextMute);
            lSpd.Location = new Point(10, 146);
            card.Controls.Add(lSpd);

            _lblSpeed = Lbl("10 %", new Font("Segoe UI", 8.5f, FontStyle.Bold), Blue);
            _lblSpeed.Location = new Point(62, 146);
            _lblSpeed.AutoSize = true;
            card.Controls.Add(_lblSpeed);

            _tbSpeed = new TrackBar
            {
                Minimum = 1, Maximum = 100, Value = 10, TickStyle = TickStyle.None,
                Location = new Point(110, 140), Width = 196, Height = 28,
                BackColor = BgPanel
            };
            _tbSpeed.ValueChanged += (s, e) => _lblSpeed.Text = _tbSpeed.Value + " %";
            card.Controls.Add(_tbSpeed);

            // 5. Nút bấm di chuyển Move
            _btnMove = Btn("▶   DI CHUYỂN TỚI TỌA ĐỘ", Blue, 296, 36);
            _btnMove.Location = new Point(10, 192);
            _btnMove.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            _btnMove.Click += async (s, e) => await OnMove();
            card.Controls.Add(_btnMove);
            _btnMove.BringToFront();

            return card;
        }

        // =================================================================
        //  COLUMN 2: MANUAL JOG & WORKSPACE INFO (Tab 1: 🎮 Điều khiển)
        // =================================================================
        private double _jogStep = 5.0;
        private Label  _lblJogStep;

        Panel BuildMiddle()
        {
            var p = new Panel { Dock = DockStyle.Fill, BackColor = BgDark, Padding = new Padding(0), Margin = new Padding(0), AutoScroll = true };

            var stackMid = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = BgDark,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            stackMid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            stackMid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            stackMid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            stackMid.Controls.Add(BuildJogCard(), 0, 0);
            stackMid.Controls.Add(BuildWorkspaceInfoCard(), 0, 1);

            p.Controls.Add(stackMid);
            return p;
        }

        Panel BuildJogCard()
        {
            var card = Card("BÀN PHÍM JOG THỦ CÔNG (MANUAL JOG CONTROLLER)", 800, 240);
            card.Dock = DockStyle.Top;
            card.Height = 240;
            card.Margin = new Padding(0, 0, 0, 6);

            // 1. Chọn bước nhảy (Step size)
            var lStep = Lbl("Bước nhảy (Step):", new Font("Segoe UI", 8.5f, FontStyle.Bold), TextMute);
            lStep.Location = new Point(14, 26);
            card.Controls.Add(lStep);

            _lblJogStep = Lbl("5.0 mm", new Font("Segoe UI", 9f, FontStyle.Bold), Blue);
            _lblJogStep.Location = new Point(130, 26);
            card.Controls.Add(_lblJogStep);

            double[] steps = { 0.1, 0.5, 1.0, 5.0, 10.0, 50.0 };
            int sx = 200;
            var stepBtns = new List<Button>();
            foreach (var st in steps)
            {
                double captured = st;
                var b = Btn($"{st} mm", (st == 5.0 ? Blue : BgPanel), 66, 24);
                b.Font = new Font("Segoe UI", 8f, FontStyle.Bold);
                b.Location = new Point(sx, 22);
                stepBtns.Add(b);
                b.Click += (s, e) =>
                {
                    _jogStep = captured;
                    _lblJogStep.Text = $"{captured} mm";
                    foreach (var btn in stepBtns)
                    {
                        bool active = (btn == b);
                        btn.BackColor = active ? Blue : BgPanel;
                        btn.ForeColor = active ? Color.White : TextMain;
                    }
                    AppendLog($"[JOG] Đã chọn bước dịch: {_jogStep} mm");
                };
                card.Controls.Add(b);
                sx += 72;
            }

            // 2. Các nút di chuyển theo trục
            // Trục X
            var lX = Lbl("Trục X:", new Font("Segoe UI", 8.5f, FontStyle.Bold), TextMain);
            lX.Location = new Point(14, 62); card.Controls.Add(lX);
            var btnXDec = Btn("◀  X -  (Vào trong)", BgPanel, 140, 30);
            var btnXInc = Btn("X +  (Ra ngoài)  ▶", BgPanel, 140, 30);
            btnXDec.Location = new Point(80, 56); btnXInc.Location = new Point(226, 56);
            btnXDec.Click += (s, e) => Jog("X", -1); btnXInc.Click += (s, e) => Jog("X", +1);
            card.Controls.Add(btnXDec); card.Controls.Add(btnXInc);

            // Trục Y
            var lY = Lbl("Trục Y:", new Font("Segoe UI", 8.5f, FontStyle.Bold), TextMain);
            lY.Location = new Point(390, 62); card.Controls.Add(lY);
            var btnYDec = Btn("◀  Y -  (Sang phải)", BgPanel, 140, 30);
            var btnYInc = Btn("Y +  (Sang trái)  ▶", BgPanel, 140, 30);
            btnYDec.Location = new Point(456, 56); btnYInc.Location = new Point(602, 56);
            btnYDec.Click += (s, e) => Jog("Y", -1); btnYInc.Click += (s, e) => Jog("Y", +1);
            card.Controls.Add(btnYDec); card.Controls.Add(btnYInc);

            // Trục Z
            var lZ = Lbl("Trục Z:", new Font("Segoe UI", 8.5f, FontStyle.Bold), TextMain);
            lZ.Location = new Point(14, 104); card.Controls.Add(lZ);
            var btnZInc = Btn("⬆  Z +  (Nâng lên)", Blue, 140, 30);
            var btnZDec = Btn("⬇  Z -  (Hạ xuống)", BgPanel, 140, 30);
            btnZInc.Location = new Point(80, 98); btnZDec.Location = new Point(226, 98);
            btnZInc.Click += (s, e) => Jog("Z", +1); btnZDec.Click += (s, e) => Jog("Z", -1);
            card.Controls.Add(btnZInc); card.Controls.Add(btnZDec);

            // Trục C
            var lC = Lbl("Trục C:", new Font("Segoe UI", 8.5f, FontStyle.Bold), TextMain);
            lC.Location = new Point(390, 104); card.Controls.Add(lC);
            var btnCDec = Btn("↺  C -  (Xoay -)", BgPanel, 140, 30);
            var btnCInc = Btn("↻  C +  (Xoay +)", BgPanel, 140, 30);
            btnCDec.Location = new Point(456, 98); btnCInc.Location = new Point(602, 98);
            btnCDec.Click += (s, e) => Jog("C", -1); btnCInc.Click += (s, e) => Jog("C", +1);
            card.Controls.Add(btnCDec); card.Controls.Add(btnCInc);

            // 3. Phím tắt Home và Nạp vào ô đích
            var btnHome = Btn("🏠  VỀ VỊ TRÍ HOME AN TOÀN (180, 0, 85, 0)", Blue, 286, 32);
            btnHome.Location = new Point(80, 142);
            btnHome.Click += (s, e) => GoHome();
            card.Controls.Add(btnHome);

            var btnCopyRight = Btn("📍  LẤY VỊ TRÍ NÀY NẠP VÀO Ô ĐÍCH BÊN PHẢI", Green, 286, 32);
            btnCopyRight.Location = new Point(456, 142);
            btnCopyRight.Click += (s, e) => CopyFeedbackToInputs();
            card.Controls.Add(btnCopyRight);

            // 4. Mẹo hướng dẫn
            var hint = Lbl("💡 Mẹo: Dùng bàn phím Jog này để căn chỉnh vị trí ngòi bút. Bảng 'DI CHUYỂN TỚI TỌA ĐỘ' bên phải luôn sẵn sàng để nhập tọa độ đích bất kỳ.", new Font("Segoe UI", 8.5f), TextMute);
            hint.Location = new Point(14, 188);
            hint.AutoSize = true;
            card.Controls.Add(hint);

            return card;
        }

        Panel BuildWorkspaceInfoCard()
        {
            var card = Card("THÔNG TIN VÙNG LÀM VIỆC & AN TOÀN (TOSHIBA MACHINE THL300)", 800, 150);
            card.Dock = DockStyle.Top;
            card.Height = 150;
            card.Margin = new Padding(0, 0, 0, 6);

            var l1 = Lbl("• Bán kính làm việc an toàn của tay máy: R = √(X² + Y²) từ 105.0 mm đến 295.0 mm (Preflight Guard bảo vệ)", new Font("Segoe UI", 9f), TextMain);
            l1.Location = new Point(14, 26); card.Controls.Add(l1);

            var l2 = Lbl("• Hành trình trục Z: 0.0 mm (kịch sàn/bàn) đến 160.0 mm (kịch trần). Quy ước: Z càng lớn thì ngòi bút càng lên cao.", new Font("Segoe UI", 9f), TextMain);
            l2.Location = new Point(14, 50); card.Controls.Add(l2);

            var l3 = Lbl("• Góc quay khớp Joint: J1 = ±125°, J2 = ±145°, J4 / C = ±360°.", new Font("Segoe UI", 9f), TextMain);
            l3.Location = new Point(14, 74); card.Controls.Add(l3);

            var l4 = Lbl("• Chế độ Cartesian: Robot tự động nội suy đường thẳng (Linear) di chuyển đồng bộ 4 trục tới X, Y, Z, C đích.", new Font("Segoe UI", 9f), TextMute);
            l4.Location = new Point(14, 98); card.Controls.Add(l4);

            var l5 = Lbl("• Chế độ Joint: Robot quay từng khớp động cơ độc lập theo góc J1, J2, J3, J4 qua lệnh Movea.", new Font("Segoe UI", 9f), TextMute);
            l5.Location = new Point(14, 122); card.Controls.Add(l5);

            return card;
        }

        void Jog(string axis, double sign)
        {
            if (!_robot.IsConnected || !_robot.IsServoOn || !_robot.IsWatchdog)
            {
                AppendLog("[WARN] Cần kết nối, bật Servo và Watchdog trước khi Jog!");
                return;
            }
            var fb = _robot.GetFeedback();
            if (fb == null)
            {
                AppendLog("[WARN] Chưa nhận được tọa độ phản hồi từ robot!");
                return;
            }

            double delta = sign * _jogStep;
            double nx = fb.X, ny = fb.Y, nz = fb.Z, nc = fb.C;
            if (axis == "X") nx += delta;
            else if (axis == "Y") ny += delta;
            else if (axis == "Z") nz += delta;
            else if (axis == "C") nc += delta;

            double r = Math.Sqrt(nx * nx + ny * ny);
            if (r < 105.0 || r > 295.0)
            {
                AppendLog($"[WARN] Vị trí sau Jog (R={r:F1}mm) nằm ngoài dải an toàn (105~295mm)!");
                return;
            }
            if (nz < 0 || nz > 160)
            {
                AppendLog($"[WARN] Độ cao Z={nz:F1}mm nằm ngoài hành trình cho phép (0~160mm)!");
                return;
            }

            int spd = _tbSpeed != null ? _tbSpeed.Value : 10;
            Task.Run(() => _robot.MoveLinear(nx, ny, nz, nc, spd));
            AppendLog($"[JOG] Trục {axis} {(delta > 0 ? "+" : "")}{delta:F1} mm → Tọa độ mới: X={nx:F1}, Y={ny:F1}, Z={nz:F1}, C={nc:F1}");
        }

        void GoHome()
        {
            if (!_robot.IsConnected || !_robot.IsServoOn || !_robot.IsWatchdog)
            {
                AppendLog("[WARN] Cần kết nối, bật Servo và Watchdog trước khi về Home!");
                return;
            }
            int spd = _tbSpeed != null ? _tbSpeed.Value : 10;
            Task.Run(() => _robot.MoveLinear(180.0, 0.0, 85.0, 0.0, spd));
            AppendLog("[INFO] Đang di chuyển về vị trí Home an toàn: X=180.0, Y=0.0, Z=85.0, C=0.0");
        }

        // =================================================================
        //  COLUMN 3: SCARA PLOTTER (Tab 2)
        // =================================================================
        Panel BuildPlotterColumn()
        {
            var outer = new Panel { Dock = DockStyle.Fill, BackColor = BgDark, Padding = new Padding(0), Margin = new Padding(0) };
            var card = Card("BÀI TẬP VẼ HÌNH HỌC (SCARA PLOTTER)", 800, 600);
            card.Dock = DockStyle.Fill;
            card.Margin = new Padding(0);
            outer.Controls.Add(card);

            // ── Khối 1: Tọa độ Tâm vẽ (Xc, Yc) & Nút Lấy Tâm ──────────────
            int y1 = 30;
            var lT1 = Lbl("1. TÂM VẼ:", new Font("Segoe UI", 8.5f, FontStyle.Bold), Orange);
            lT1.Location = new Point(12, y1); card.Controls.Add(lT1);

            var lXc = Lbl("Xc", new Font("Segoe UI", 8f), TextMute);
            lXc.Location = new Point(90, y1); card.Controls.Add(lXc);
            _txtPlotXc = Input("180.0", 68);
            _txtPlotXc.Location = new Point(112, y1 - 2); card.Controls.Add(_txtPlotXc);

            var lYc = Lbl("Yc", new Font("Segoe UI", 8f), TextMute);
            lYc.Location = new Point(190, y1); card.Controls.Add(lYc);
            _txtPlotYc = Input("0.0", 68);
            _txtPlotYc.Location = new Point(212, y1 - 2); card.Controls.Add(_txtPlotYc);

            _btnPlotSetCenter = Btn("📍 LẤY XY HIỆN TẠI LÀM TÂM", Green, 220, 28);
            _btnPlotSetCenter.Font = new Font("Segoe UI", 8f, FontStyle.Bold);
            _btnPlotSetCenter.Location = new Point(290, y1 - 2);
            _btnPlotSetCenter.Click += (s, e) =>
            {
                var fb = _robot.GetFeedback();
                if (fb != null)
                {
                    _txtPlotXc.Text = fb.X.ToString("F1", CultureInfo.InvariantCulture);
                    _txtPlotYc.Text = fb.Y.ToString("F1", CultureInfo.InvariantCulture);
                    AppendLog($"[PLOT] Đã đặt tâm vẽ: Xc={fb.X:F1}, Yc={fb.Y:F1}");
                }
            };
            card.Controls.Add(_btnPlotSetCenter);

            // ── Khối 2: Tách biệt kích thước từng hình ────────────────────
            int y2 = y1 + 38;
            var lT2 = Lbl("2. KÍCH THƯỚC TỪNG HÌNH (mm):", new Font("Segoe UI", 8.5f, FontStyle.Bold), Orange);
            lT2.Location = new Point(12, y2); card.Controls.Add(lT2);

            // Hình vuông / Chữ X: Cạnh
            var lSq = Lbl("Cạnh Vuông/X:", new Font("Segoe UI", 8f), TextMute);
            lSq.Location = new Point(12, y2 + 24); card.Controls.Add(lSq);
            _txtSquareSize = Input("40.0", 64);
            _txtSquareSize.Location = new Point(106, y2 + 22); card.Controls.Add(_txtSquareSize);

            // Hình tròn: Đường kính & Số đoạn
            var lCir = Lbl("Đ.Kính Tròn:", new Font("Segoe UI", 8f), TextMute);
            lCir.Location = new Point(184, y2 + 24); card.Controls.Add(lCir);
            _txtCircleDia = Input("40.0", 64);
            _txtCircleDia.Location = new Point(262, y2 + 22); card.Controls.Add(_txtCircleDia);

            var lSeg = Lbl("Số đoạn:", new Font("Segoe UI", 8f), TextMute);
            lSeg.Location = new Point(334, y2 + 24); card.Controls.Add(lSeg);
            _txtCircleSegs = Input("36", 46);
            _txtCircleSegs.Location = new Point(390, y2 + 22); card.Controls.Add(_txtCircleSegs);

            // Tam giác: Chiều cao
            var lTri = Lbl("Đ.Cao Tam Giác:", new Font("Segoe UI", 8f), TextMute);
            lTri.Location = new Point(446, y2 + 24); card.Controls.Add(lTri);
            _txtTriHeight = Input("35.0", 56);
            _txtTriHeight.Location = new Point(544, y2 + 22); card.Controls.Add(_txtTriHeight);

            // ── Khối 3: Cài đặt trục Z (Bút vẽ) & Tinh chỉnh ───────────────
            int y3 = y2 + 60;
            var lT3 = Lbl("3. CÀI ĐẶT ĐỘ CAO TRỤC Z (PEN SETTING):", new Font("Segoe UI", 8.5f, FontStyle.Bold), Orange);
            lT3.Location = new Point(12, y3); card.Controls.Add(lT3);

            var lZd = Lbl("Z Hạ Bút", new Font("Segoe UI", 8f), TextMute);
            lZd.Location = new Point(12, y3 + 24); card.Controls.Add(lZd);
            _txtPlotZDraw = Input("140.0", 66);
            _txtPlotZDraw.Location = new Point(72, y3 + 22); card.Controls.Add(_txtPlotZDraw);

            _btnPlotSetZDraw = Btn("📍 LẤY Z HIỆN TẠI", Green, 136, 26);
            _btnPlotSetZDraw.Font = new Font("Segoe UI", 7.5f, FontStyle.Bold);
            _btnPlotSetZDraw.Location = new Point(144, y3 + 22);
            _btnPlotSetZDraw.Click += (s, e) =>
            {
                var fb = _robot.GetFeedback();
                if (fb != null)
                {
                    _txtPlotZDraw.Text = fb.Z.ToString("F1", CultureInfo.InvariantCulture);
                    // Tự động tính độ cao nhấc bút an toàn: nhấc cao hơn mặt giấy 20mm (kịch trần là Z=0 nên trừ đi)
                    double safeLift = Math.Max(30.0, fb.Z - 20.0);
                    _txtPlotZLift.Text = safeLift.ToString("F1", CultureInfo.InvariantCulture);
                    AppendLog($"[PLOT] Đã chốt Z hạ: {fb.Z:F1} mm | Tự động đặt Z nhấc an toàn: {safeLift:F1} mm (cách giấy 20mm)");
                }
            };
            card.Controls.Add(_btnPlotSetZDraw);

            var lZl = Lbl("Z Nhấc", new Font("Segoe UI", 8f), TextMute);
            lZl.Location = new Point(292, y3 + 24); card.Controls.Add(lZl);
            _txtPlotZLift = Input("85.0", 60);
            _txtPlotZLift.Location = new Point(340, y3 + 22); card.Controls.Add(_txtPlotZLift);

            _btnPlotSetZLift = Btn("📍 NHẤC 20mm", BgPanel, 102, 26);
            _btnPlotSetZLift.Font = new Font("Segoe UI", 7.5f, FontStyle.Bold);
            _btnPlotSetZLift.Location = new Point(406, y3 + 22);
            _btnPlotSetZLift.Click += (s, e) =>
            {
                if (double.TryParse(_txtPlotZDraw.Text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double zd))
                {
                    double safeLift = Math.Max(30.0, zd - 20.0);
                    _txtPlotZLift.Text = safeLift.ToString("F1", CultureInfo.InvariantCulture);
                    AppendLog($"[PLOT] Đã đặt Z nhấc: {safeLift:F1} mm (cách Z hạ 20mm)");
                }
                else
                {
                    _txtPlotZLift.Text = "85.0";
                }
            };
            card.Controls.Add(_btnPlotSetZLift);

            // Nudge buttons Z
            int yNudge = y3 + 54;
            var lNud = Lbl("Tinh chỉnh Z:", new Font("Segoe UI", 8f), TextMute);
            lNud.Location = new Point(12, yNudge + 4); card.Controls.Add(lNud);

            _btnPlotZInc1  = Btn("▲ +1mm",   BgPanel, 64, 26);
            _btnPlotZDec1  = Btn("▼ -1mm",   BgPanel, 64, 26);
            _btnPlotZInc05 = Btn("▲ +0.5mm", BgPanel, 72, 26);
            _btnPlotZDec05 = Btn("▼ -0.5mm", BgPanel, 72, 26);

            _btnPlotZInc1.Font  = _btnPlotZDec1.Font  = new Font("Segoe UI", 7.5f, FontStyle.Bold);
            _btnPlotZInc05.Font = _btnPlotZDec05.Font = new Font("Segoe UI", 7.5f, FontStyle.Bold);

            _btnPlotZInc1.Location  = new Point(96,  yNudge);
            _btnPlotZDec1.Location  = new Point(164, yNudge);
            _btnPlotZInc05.Location = new Point(232, yNudge);
            _btnPlotZDec05.Location = new Point(308, yNudge);

            _btnPlotZInc1.Click  += (s, e) => NudgeZ(1.0);
            _btnPlotZDec1.Click  += (s, e) => NudgeZ(-1.0);
            _btnPlotZInc05.Click += (s, e) => NudgeZ(0.5);
            _btnPlotZDec05.Click += (s, e) => NudgeZ(-0.5);

            card.Controls.Add(_btnPlotZInc1);
            card.Controls.Add(_btnPlotZDec1);
            card.Controls.Add(_btnPlotZInc05);
            card.Controls.Add(_btnPlotZDec05);

            // Test buttons
            _btnPlotPenDown = Btn("⬇ HẠ THỬ", Blue, 90, 26);
            _btnPlotPenUp   = Btn("⬆ NHẤC AN TOÀN", BgPanel, 114, 26);
            _btnPlotPenDown.Font = _btnPlotPenUp.Font = new Font("Segoe UI", 7.5f, FontStyle.Bold);
            _btnPlotPenDown.Location = new Point(390, yNudge);
            _btnPlotPenUp.Location   = new Point(486, yNudge);

            _btnPlotPenDown.Click += async (s, e) => await TestPenDown();
            _btnPlotPenUp.Click   += async (s, e) => await TestPenUp();
            card.Controls.Add(_btnPlotPenDown);
            card.Controls.Add(_btnPlotPenUp);

            // ── Khối 4: Tốc độ vẽ & Checkboxes ────────────────────────────
            int y4 = yNudge + 34;
            _lblPlotSpeed = Lbl("Tốc độ vẽ: 10 %", new Font("Segoe UI", 8.5f, FontStyle.Bold), Blue);
            _lblPlotSpeed.Location = new Point(12, y4 + 4); card.Controls.Add(_lblPlotSpeed);

            _tbPlotSpeed = new TrackBar
            {
                Minimum = 1, Maximum = 50, Value = 10, TickStyle = TickStyle.None,
                Location = new Point(120, y4), Width = 150, Height = 26
            };
            _tbPlotSpeed.ValueChanged += (s, e) => _lblPlotSpeed.Text = $"Tốc độ vẽ: {_tbPlotSpeed.Value} %";
            card.Controls.Add(_tbPlotSpeed);

            _chkPauseOnPenDown = new CheckBox
            {
                Text = "Tạm dừng sau khi hạ bút để kiểm tra ngòi",
                AutoSize = true, Checked = false,
                Font = new Font("Segoe UI", 8f),
                ForeColor = TextMain, Location = new Point(285, y4 + 3)
            };
            card.Controls.Add(_chkPauseOnPenDown);

            _chkDryRun = new CheckBox
            {
                Text = "Chạy thử trên không (Dry Run)",
                AutoSize = true, Checked = false,
                Font = new Font("Segoe UI", 8f, FontStyle.Bold),
                ForeColor = Orange, Location = new Point(285, y4 + 23)
            };
            card.Controls.Add(_chkDryRun);

            // ── Khối 5: 4 Nút Vẽ Hình ──────────────────────────────────────
            int y5 = y4 + 46;
            _btnDrawSquare   = Btn("🔲 VẼ HÌNH VUÔNG", Blue, 142, 34);
            _btnDrawTriangle = Btn("🔺 VẼ TAM GIÁC",   Blue, 142, 34);
            _btnDrawX        = Btn("❌ VẼ CHỮ X",      Blue, 142, 34);
            _btnDrawCircle   = Btn("⭕ VẼ HÌNH TRÒN",   Blue, 142, 34);

            _btnDrawSquare.Location   = new Point(12,  y5);
            _btnDrawTriangle.Location = new Point(158, y5);
            _btnDrawX.Location        = new Point(304, y5);
            _btnDrawCircle.Location   = new Point(450, y5);

            _btnDrawSquare.Click   += async (s, e) => await StartDraw(ShapeType.Square);
            _btnDrawTriangle.Click += async (s, e) => await StartDraw(ShapeType.Triangle);
            _btnDrawX.Click        += async (s, e) => await StartDraw(ShapeType.LetterX);
            _btnDrawCircle.Click   += async (s, e) => await StartDraw(ShapeType.Circle);

            card.Controls.Add(_btnDrawSquare);
            card.Controls.Add(_btnDrawTriangle);
            card.Controls.Add(_btnDrawX);
            card.Controls.Add(_btnDrawCircle);

            // ── Khối 6: Nút Tiếp tục & Dừng vẽ khẩn cấp ────────────────────
            int y6 = y5 + 42;
            _btnContinueDraw = Btn("▶ TIẾP TỤC VẼ (sau khi tạm dừng)", Green, 260, 32);
            _btnContinueDraw.Location = new Point(12, y6);
            _btnContinueDraw.Enabled = false;
            _btnContinueDraw.Click += (s, e) =>
            {
                _btnContinueDraw.Enabled = false;
                _continueDrawEvent.Set();
                AppendLog("[PLOT] Người dùng bấm TIẾP TỤC VẼ.");
            };
            card.Controls.Add(_btnContinueDraw);

            _btnAbortDraw = Btn("⛔ DỪNG VẼ & MỞ KHÓA NGAY", Red, 308, 32);
            _btnAbortDraw.Location = new Point(284, y6);
            _btnAbortDraw.Enabled = false;
            _btnAbortDraw.Click += (s, e) =>
            {
                AppendLog("[PLOT] ⛔ ĐÃ BẤM DỪNG VẼ & CƯỠNG CHẾ MỞ KHÓA!");
                try { _drawCts?.Cancel(); } catch { }
                _robot.CancelDraw();
                _robot.ProgramStop();
                _continueDrawEvent.Set();
                _isDrawing = false;
                _robot.ResetMove();
                _robot.ResetAlarm();
                _btnContinueDraw.Enabled = false;
                UpdateStatus();
                AppendLog("[PLOT] ✔ Toàn bộ các nút vẽ đã được mở khóa! Bạn có thể tiếp tục thao tác.");
            };
            card.Controls.Add(_btnAbortDraw);

            return outer;
        }

        // =================================================================
        //  SEQUENCER PANEL — Bảng điểm Dạy & Chạy Dây Chuyền (Tab 3)
        // =================================================================
        Panel BuildSequencerPanel()
        {
            var outer = new Panel { Dock = DockStyle.Fill, BackColor = BgDark, Padding = new Padding(0), Margin = new Padding(0) };
            var card  = Card("BẢNG ĐIỂM DẠY & DÂY CHUYỀN (SEQUENCER)", 900, 600);
            card.Dock = DockStyle.Fill;
            card.Margin = new Padding(0);
            outer.Controls.Add(card);

            // ── DataGridView điểm ─────────────────────────────────────────
            _dgvPoints = new DataGridView
            {
                Location  = new Point(12, 34),
                Height    = 220,
                Anchor    = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                BackgroundColor = Color.White,
                ForeColor = TextMain,
                GridColor = Color.FromArgb(226, 232, 240),
                BorderStyle = BorderStyle.FixedSingle,
                RowHeadersVisible = false,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                Font = new Font("Consolas", 9f),
                ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
                {
                    BackColor = Color.FromArgb(241, 245, 249),
                    ForeColor = Color.FromArgb(71, 85, 105),
                    Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                    SelectionBackColor = Color.FromArgb(241, 245, 249)
                },
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    BackColor = Color.White,
                    ForeColor = TextMain,
                    SelectionBackColor = Color.FromArgb(219, 234, 254),
                    SelectionForeColor = Color.FromArgb(30, 58, 138)
                },
                RowsDefaultCellStyle = new DataGridViewCellStyle
                {
                    BackColor = Color.White,
                    ForeColor = TextMain
                },
                AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
                {
                    BackColor = Color.FromArgb(248, 250, 252),
                    ForeColor = TextMain
                }
            };
            // Columns
            _dgvPoints.Columns.Add(new DataGridViewTextBoxColumn { Name = "colName",   HeaderText = "Tên",    FillWeight = 18 });
            _dgvPoints.Columns.Add(new DataGridViewTextBoxColumn { Name = "colX",      HeaderText = "X (mm)", FillWeight = 13 });
            _dgvPoints.Columns.Add(new DataGridViewTextBoxColumn { Name = "colY",      HeaderText = "Y (mm)", FillWeight = 13 });
            _dgvPoints.Columns.Add(new DataGridViewTextBoxColumn { Name = "colZ",      HeaderText = "Z (mm)", FillWeight = 13 });
            _dgvPoints.Columns.Add(new DataGridViewTextBoxColumn { Name = "colC",      HeaderText = "C (°)",  FillWeight = 11 });
            _dgvPoints.Columns.Add(new DataGridViewTextBoxColumn { Name = "colSpd",    HeaderText = "Spd%",   FillWeight = 9  });
            _dgvPoints.Columns.Add(new DataGridViewTextBoxColumn { Name = "colDelay",  HeaderText = "Delay ms", FillWeight = 11 });
            var colMotion = new DataGridViewComboBoxColumn
            {
                Name = "colMotion", HeaderText = "Kiểu",
                Items = { "Linear", "PTP" },
                FillWeight = 12,
                DefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.White, ForeColor = TextMain }
            };
            _dgvPoints.Columns.Add(colMotion);
            _dgvPoints.CellEndEdit += (s, e) => SyncGridToPoints(e.RowIndex);

            card.Controls.Add(_dgvPoints);

            // ── Toolbar hàng 1: Add/Delete/Up/Down/Teach ─────────────────
            int ty = 264;
            var btnF = new Font("Segoe UI", 8f, FontStyle.Bold);

            _btnSeqAddPoint = Btn("➕ Thêm điểm",  Green,  110, 28);
            _btnSeqAddPoint.Font = btnF;
            _btnSeqAddPoint.Location = new Point(12, ty);
            _btnSeqAddPoint.Click += (s, e) => SeqAddPoint();
            card.Controls.Add(_btnSeqAddPoint);

            _btnSeqDeletePoint = Btn("🗑 Xóa",    Red,    70, 28);
            _btnSeqDeletePoint.Font = btnF;
            _btnSeqDeletePoint.Location = new Point(128, ty);
            _btnSeqDeletePoint.Click += (s, e) => SeqDeletePoint();
            card.Controls.Add(_btnSeqDeletePoint);

            _btnSeqMoveUp = Btn("▲ Lên",   BgPanel, 64, 28);
            _btnSeqMoveUp.Font = btnF;
            _btnSeqMoveUp.Location = new Point(204, ty);
            _btnSeqMoveUp.Click += (s, e) => SeqMovePoint(-1);
            card.Controls.Add(_btnSeqMoveUp);

            _btnSeqMoveDown = Btn("▼ Xuống", BgPanel, 74, 28);
            _btnSeqMoveDown.Font = btnF;
            _btnSeqMoveDown.Location = new Point(274, ty);
            _btnSeqMoveDown.Click += (s, e) => SeqMovePoint(+1);
            card.Controls.Add(_btnSeqMoveDown);

            _btnSeqTeach = Btn("📍 Lấy vị trí hiện tại của Robot", Blue, 230, 28);
            _btnSeqTeach.Font = btnF;
            _btnSeqTeach.Location = new Point(354, ty);
            _btnSeqTeach.Click += (s, e) => SeqTeachCurrentPoint();
            card.Controls.Add(_btnSeqTeach);

            _btnSeqSave = Btn("💾 Lưu CSV", BgPanel, 84, 28);
            _btnSeqSave.Font = btnF;
            _btnSeqSave.Location = new Point(590, ty);
            _btnSeqSave.Click += (s, e) => SeqSave();
            card.Controls.Add(_btnSeqSave);

            _btnSeqLoad = Btn("📂 Mở CSV", BgPanel, 84, 28);
            _btnSeqLoad.Font = btnF;
            _btnSeqLoad.Location = new Point(680, ty);
            _btnSeqLoad.Click += (s, e) => SeqLoad();
            card.Controls.Add(_btnSeqLoad);

            // ── Toolbar hàng 2: Z Nhấc giữa điểm + Run/Stop/Loop ─────────
            int ty2 = ty + 36;

            var lZnhac = Lbl("Z an toàn chuyển điểm:", new Font("Segoe UI", 8.5f, FontStyle.Bold), TextMute);
            lZnhac.Location = new Point(12, ty2 + 4);
            card.Controls.Add(lZnhac);

            _txtSeqZLift = Input("30.0", 64);
            _txtSeqZLift.Font = new Font("Consolas", 9.5f);
            _txtSeqZLift.Location = new Point(176, ty2 + 2);
            card.Controls.Add(_txtSeqZLift);

            var lMm = Lbl("mm", new Font("Segoe UI", 8.5f), TextMute);
            lMm.Location = new Point(246, ty2 + 4);
            card.Controls.Add(lMm);

            _chkSeqLoop = new CheckBox
            {
                Text = "🔁 Lặp lại chu kỳ liên tục",
                AutoSize = true, Checked = false,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = Orange,
                Location = new Point(286, ty2 + 3)
            };
            card.Controls.Add(_chkSeqLoop);

            _btnSeqRun = Btn("▶ CHẠY DÂY CHUYỀN", Green, 180, 32);
            _btnSeqRun.Location = new Point(12, ty2 + 36);
            _btnSeqRun.Click += async (s, e) => await SeqRun();
            card.Controls.Add(_btnSeqRun);

            _btnSeqStop = Btn("⛔ DỪNG NGAY", Red, 120, 32);
            _btnSeqStop.Location = new Point(198, ty2 + 36);
            _btnSeqStop.Enabled = false;
            _btnSeqStop.Click += (s, e) => SeqForceStop();
            card.Controls.Add(_btnSeqStop);

            _lblSeqStatus = Lbl("Trạng thái: Sẵn sàng", new Font("Segoe UI", 9f, FontStyle.Bold), TextMute);
            _lblSeqStatus.Location = new Point(330, ty2 + 42);
            card.Controls.Add(_lblSeqStatus);

            card.Resize += (s, e) =>
            {
                _dgvPoints.Width = card.ClientSize.Width - 24;
            };

            return outer;
        }

        // ── SEQ helper methods ───────────────────────────────────────────────

        void RefreshGrid()
        {
            _dgvPoints.Rows.Clear();
            for (int i = 0; i < _points.Count; i++)
            {
                var p = _points[i];
                int idx = _dgvPoints.Rows.Add(
                    p.Name,
                    p.X.ToString("F1", CultureInfo.InvariantCulture),
                    p.Y.ToString("F1", CultureInfo.InvariantCulture),
                    p.Z.ToString("F1", CultureInfo.InvariantCulture),
                    p.C.ToString("F1", CultureInfo.InvariantCulture),
                    p.Speed.ToString(),
                    p.DelayMs.ToString(),
                    p.MotionType);
            }
        }

        void SyncGridToPoints(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= _dgvPoints.Rows.Count || rowIndex >= _points.Count) return;
            var row = _dgvPoints.Rows[rowIndex];
            var pt  = _points[rowIndex];

            pt.Name = row.Cells["colName"].Value?.ToString() ?? pt.Name;
            if (double.TryParse(row.Cells["colX"].Value?.ToString()?.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double x)) pt.X = x;
            if (double.TryParse(row.Cells["colY"].Value?.ToString()?.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double y)) pt.Y = y;
            if (double.TryParse(row.Cells["colZ"].Value?.ToString()?.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double z)) pt.Z = z;
            if (double.TryParse(row.Cells["colC"].Value?.ToString()?.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double c)) pt.C = c;
            if (int.TryParse(row.Cells["colSpd"].Value?.ToString(), out int spd))    pt.Speed = Math.Max(1, Math.Min(100, spd));
            if (int.TryParse(row.Cells["colDelay"].Value?.ToString(), out int del))  pt.DelayMs = Math.Max(0, del);
            var mt = row.Cells["colMotion"].Value?.ToString();
            if (mt == "Linear" || mt == "PTP") pt.MotionType = mt;
        }

        void SeqAddPoint()
        {
            var fb = _robot.GetFeedback();
            int idx = _points.Count + 1;
            var pt = fb != null
                ? new TeachPoint($"P{idx}", fb.X, fb.Y, fb.Z, fb.C)
                : new TeachPoint($"P{idx}", 0, 0, 0, 0);
            _points.Add(pt);
            RefreshGrid();
            _dgvPoints.ClearSelection();
            if (_dgvPoints.Rows.Count > 0)
                _dgvPoints.Rows[_dgvPoints.Rows.Count - 1].Selected = true;
            AppendLog($"[SEQ] Đã thêm điểm '{pt.Name}' ({pt.X:F1}, {pt.Y:F1}, {pt.Z:F1})");
        }

        void SeqDeletePoint()
        {
            int sel = _dgvPoints.CurrentRow?.Index ?? -1;
            if (sel < 0 || sel >= _points.Count) return;
            string name = _points[sel].Name;
            _points.RemoveAt(sel);
            RefreshGrid();
            AppendLog($"[SEQ] Đã xóa điểm '{name}'");
        }

        void SeqMovePoint(int dir)
        {
            int sel = _dgvPoints.CurrentRow?.Index ?? -1;
            int newIdx = sel + dir;
            if (sel < 0 || newIdx < 0 || newIdx >= _points.Count) return;
            var tmp = _points[sel];
            _points[sel] = _points[newIdx];
            _points[newIdx] = tmp;
            RefreshGrid();
            _dgvPoints.ClearSelection();
            if (newIdx < _dgvPoints.Rows.Count) _dgvPoints.Rows[newIdx].Selected = true;
        }

        void SeqTeachCurrentPoint()
        {
            int sel = _dgvPoints.CurrentRow?.Index ?? -1;
            if (sel < 0 || sel >= _points.Count)
            {
                AppendLog("[SEQ] Chọn một điểm trong bảng trước khi lấy vị trí hiện tại!");
                return;
            }
            var fb = _robot.GetFeedback();
            if (fb == null) { AppendLog("[SEQ] Chưa kết nối robot — không thể lấy vị trí!"); return; }
            var pt = _points[sel];
            pt.X = fb.X; pt.Y = fb.Y; pt.Z = fb.Z; pt.C = fb.C;
            RefreshGrid();
            _dgvPoints.ClearSelection();
            _dgvPoints.Rows[sel].Selected = true;
            AppendLog($"[SEQ] Đã dạy điểm '{pt.Name}': X={fb.X:F1}, Y={fb.Y:F1}, Z={fb.Z:F1}, C={fb.C:F1}");
        }

        void SeqSave()
        {
            using (var dlg = new SaveFileDialog
            {
                Title = "Lưu danh sách điểm",
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                DefaultExt = "csv",
                FileName = "teach_points.csv"
            })
            {
                if (dlg.ShowDialog() != DialogResult.OK) return;
                try
                {
                    var lines = new List<string> { "Name,X,Y,Z,C,Speed,DelayMs,MotionType" };
                    lines.AddRange(_points.Select(p =>
                        $"{p.Name},{p.X.ToString("F4", CultureInfo.InvariantCulture)},{p.Y.ToString("F4", CultureInfo.InvariantCulture)}," +
                        $"{p.Z.ToString("F4", CultureInfo.InvariantCulture)},{p.C.ToString("F4", CultureInfo.InvariantCulture)}," +
                        $"{p.Speed},{p.DelayMs},{p.MotionType}"));
                    File.WriteAllLines(dlg.FileName, lines, System.Text.Encoding.UTF8);
                    AppendLog($"[SEQ] Đã lưu {_points.Count} điểm → {dlg.FileName}");
                }
                catch (Exception ex) { AppendLog($"[SEQ][ERROR] Lỗi lưu file: {ex.Message}"); }
            }
        }

        void SeqLoad()
        {
            using (var dlg = new OpenFileDialog
            {
                Title = "Mở danh sách điểm",
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*"
            })
            {
                if (dlg.ShowDialog() != DialogResult.OK) return;
                try
                {
                    var lines = File.ReadAllLines(dlg.FileName, System.Text.Encoding.UTF8);
                    var loaded = new List<TeachPoint>();
                    foreach (var line in lines.Skip(1)) // bỏ header
                    {
                        var f = line.Split(',');
                        if (f.Length < 8) continue;
                        if (!double.TryParse(f[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double lx)) continue;
                        if (!double.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double ly)) continue;
                        if (!double.TryParse(f[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double lz)) continue;
                        if (!double.TryParse(f[4], NumberStyles.Float, CultureInfo.InvariantCulture, out double lc)) continue;
                        int.TryParse(f[5], out int spd); int.TryParse(f[6], out int del);
                        var mt = f[7].Trim() == "PTP" ? "PTP" : "Linear";
                        loaded.Add(new TeachPoint(f[0].Trim(), lx, ly, lz, lc, Math.Max(1, spd), Math.Max(0, del), mt));
                    }
                    _points = loaded;
                    RefreshGrid();
                    AppendLog($"[SEQ] Đã nạp {_points.Count} điểm từ {dlg.FileName}");
                }
                catch (Exception ex) { AppendLog($"[SEQ][ERROR] Lỗi đọc file: {ex.Message}"); }
            }
        }

        void SeqForceStop()
        {
            try { _seqCts?.Cancel(); } catch { }
            _robot.ProgramStop();
            _robot.ResetMove();
            _isRunningSeq = false;
            UpdateSeqStatus("⛔ Đã dừng khẩn cấp");
            UpdateStatus();
            AppendLog("[SEQ] ⛔ Đã cưỡng chế dừng dây chuyền!");
        }

        void UpdateSeqStatus(string msg)
        {
            if (_lblSeqStatus.InvokeRequired)
                _lblSeqStatus.BeginInvoke((Action)(() => { _lblSeqStatus.Text = msg; _lblSeqStatus.ForeColor = _isRunningSeq ? Color.FromArgb(34, 197, 94) : TextMute; }));
            else { _lblSeqStatus.Text = msg; _lblSeqStatus.ForeColor = _isRunningSeq ? Color.FromArgb(34, 197, 94) : TextMute; }
        }

        async Task SeqRun()
        {
            if (!_robot.IsConnected || !_robot.IsServoOn || !_robot.IsWatchdog)
            {
                AppendLog("[SEQ] Cần kết nối, bật Watchdog và Servo trước khi chạy dây chuyền!");
                return;
            }
            if (_points.Count == 0) { AppendLog("[SEQ] Bảng điểm trống! Thêm ít nhất 1 điểm."); return; }
            if (!double.TryParse(_txtSeqZLift.Text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double seqZLift))
            {
                AppendLog("[SEQ] Z an toàn không hợp lệ!"); return;
            }

            _isRunningSeq = true;
            _seqCts?.Cancel();
            _seqCts?.Dispose();
            _seqCts = new CancellationTokenSource();
            var token = _seqCts.Token;

            _btnSeqRun.Enabled  = false;
            _btnSeqStop.Enabled = true;
            UpdateStatus();

            bool loop = _chkSeqLoop.Checked;
            int cycle = 0;

            try
            {
                do
                {
                    cycle++;
                    AppendLog($"[SEQ] ══ Bắt đầu chu kỳ {cycle} / {(loop ? "∞" : "1")} ══");
                    for (int i = 0; i < _points.Count; i++)
                    {
                        if (token.IsCancellationRequested) break;
                        var pt = _points[i];

                        // Highlight row đang chạy
                        int capturedI = i;
                        _dgvPoints.BeginInvoke((Action)(() =>
                        {
                            _dgvPoints.ClearSelection();
                            if (capturedI < _dgvPoints.Rows.Count) _dgvPoints.Rows[capturedI].Selected = true;
                        }));

                        UpdateSeqStatus($"⚙ Đang tới điểm {i+1}/{_points.Count}: {pt.Name}");
                        AppendLog($"[SEQ] → Điểm {i+1}/{_points.Count}: '{pt.Name}' X={pt.X:F1} Y={pt.Y:F1} Z={pt.Z:F1} C={pt.C:F1} [{pt.MotionType}] @ {pt.Speed}%");

                        bool ok = await Task.Run(() =>
                        {
                            // 1. Nhấc lên Z an toàn trước khi bay
                            var fb = _robot.GetFeedback();
                            if (fb != null && fb.Z < seqZLift)
                            {
                                if (!_robot.MoveLinear(fb.X, fb.Y, seqZLift, fb.C, pt.Speed)) return false;
                                if (!_robot.WaitUntilReached(fb.X, fb.Y, seqZLift, 2.0, 2.0, 10000, token)) return false;
                            }
                            if (token.IsCancellationRequested) return false;

                            // 2. Bay tới XY mục tiêu ở Z an toàn
                            if (pt.MotionType == "PTP")
                            {
                                // PTP: dùng Cartesian Move (Move = PTP)
                                if (!_robot.MoveCartesian(pt.X, pt.Y, seqZLift, pt.C, pt.Speed)) return false;
                            }
                            else
                            {
                                // Linear: dùng Moves
                                if (!_robot.MoveLinear(pt.X, pt.Y, seqZLift, pt.C, pt.Speed)) return false;
                            }
                            if (!_robot.WaitUntilReached(pt.X, pt.Y, seqZLift, 1.5, 2.0, 20000, token)) return false;
                            if (token.IsCancellationRequested) return false;

                            // 3. Hạ xuống đúng Z mục tiêu
                            if (Math.Abs(pt.Z - seqZLift) > 0.5)
                            {
                                if (!_robot.MoveLinear(pt.X, pt.Y, pt.Z, pt.C, Math.Min(pt.Speed, 15))) return false;
                                if (!_robot.WaitUntilReached(pt.X, pt.Y, pt.Z, 1.5, 2.0, 12000, token)) return false;
                            }
                            if (token.IsCancellationRequested) return false;

                            // 4. Chờ Delay nếu có
                            if (pt.DelayMs > 0)
                            {
                                var sw = System.Diagnostics.Stopwatch.StartNew();
                                while (sw.ElapsedMilliseconds < pt.DelayMs)
                                {
                                    if (token.IsCancellationRequested) return false;
                                    Thread.Sleep(50);
                                }
                            }
                            return true;
                        }, token);

                        if (!ok || token.IsCancellationRequested) break;
                    }

                    if (token.IsCancellationRequested) break;

                } while (loop && !token.IsCancellationRequested);

                if (!token.IsCancellationRequested)
                    AppendLog($"[SEQ] ✔ Hoàn thành dây chuyền ({cycle} chu kỳ).");
            }
            catch (OperationCanceledException)
            {
                AppendLog("[SEQ] Đã hủy dây chuyền.");
            }
            catch (Exception ex)
            {
                AppendLog($"[SEQ][ERROR] {ex.Message}");
            }
            finally
            {
                _isRunningSeq = false;
                try { _robot.ResetMove(); } catch { }
                UpdateSeqStatus("Sẵn sàng");
                Action unlock = () =>
                {
                    _btnSeqRun.Enabled  = true;
                    _btnSeqStop.Enabled = false;
                    _dgvPoints.ClearSelection();
                    UpdateStatus();
                };
                if (InvokeRequired) Invoke(unlock); else unlock();
                AppendLog("[SEQ] ══ KẾT THÚC DÂY CHUYỀN ══");
            }
        }

        // =================================================================
        //  BOTTOM: ACTIVITY LOG (Khung lớn trải dài bên dưới khu vực giữa)
        // =================================================================
        Panel BuildBottomLog()
        {
            var p = new Panel { Dock = DockStyle.Fill, BackColor = BgDark, Padding = new Padding(0), Margin = new Padding(0) };

            // Card cấu trúc TableLayoutPanel 2 dòng: Dòng 0 (Header 34px), Dòng 1 (Khung text 100%)
            var logCard = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = BgPanel,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            logCard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            logCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f)); // Header
            logCard.RowStyles.Add(new RowStyle(SizeType.Percent, 100f)); // Body

            // 1. Header Bar: Tiêu đề + Nút Xóa Log
            var headerBar = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = BgPanel,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };

            var lTitle = new Label
            {
                Text = "NHẬT KÝ HOẠT ĐỘNG (ACTIVITY LOG)",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = TextMain,
                AutoSize = true,
                Location = new Point(12, 8)
            };
            headerBar.Controls.Add(lTitle);

            var btnClr = new Button
            {
                Text = "🗑  Xóa Log",
                FlatStyle = FlatStyle.Flat,
                Width = 84,
                Height = 22,
                ForeColor = TextMute,
                BackColor = BgCard,
                Font = new Font("Segoe UI", 8f),
                Cursor = Cursors.Hand,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(headerBar.ClientSize.Width - 96, 6)
            };
            btnClr.FlatAppearance.BorderSize = 1;
            btnClr.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
            btnClr.Click += (s, e) => _log?.Clear();
            headerBar.Controls.Add(btnClr);

            headerBar.Resize += (s, e) =>
            {
                btnClr.Location = new Point(headerBar.ClientSize.Width - 96, 6);
            };

            // 2. Body Panel: Chứa RichTextBox có viền trắng 10px đều các cạnh
            var logBody = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = BgPanel,
                Padding = new Padding(10, 0, 10, 10),
                Margin = new Padding(0)
            };

            _log = new RichTextBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(15, 23, 42),
                ForeColor = Color.FromArgb(134, 239, 172),
                Font      = new Font("Consolas", 9f),
                ReadOnly  = true,
                ScrollBars  = RichTextBoxScrollBars.Vertical,
                BorderStyle = BorderStyle.None
            };
            logBody.Controls.Add(_log);

            logCard.Controls.Add(headerBar, 0, 0);
            logCard.Controls.Add(logBody,   0, 1);

            p.Controls.Add(logCard);
            return p;
        }

        void SwitchMode(bool cartesian)
        {
            _isCartesianMode = cartesian;
            _pnlCartesian.Visible = cartesian;
            _pnlJoint.Visible     = !cartesian;

            if (cartesian)
            {
                _btnModeCart.BackColor  = Blue;
                _btnModeCart.ForeColor  = Color.White;
                _btnModeJoint.BackColor = BgPanel;
                _btnModeJoint.ForeColor = TextMain;
            }
            else
            {
                _btnModeCart.BackColor  = BgPanel;
                _btnModeCart.ForeColor  = TextMain;
                _btnModeJoint.BackColor = Blue;
                _btnModeJoint.ForeColor = Color.White;
            }
        }

        void CopyFeedbackToInputs()
        {
            var fb = _robot.GetFeedback();
            if (fb == null)
            {
                AppendLog("[WARN] Chưa có dữ liệu tọa độ từ robot (chưa kết nối).");
                return;
            }

            _txtX.Text  = fb.X.ToString("F2", CultureInfo.InvariantCulture);
            _txtY.Text  = fb.Y.ToString("F2", CultureInfo.InvariantCulture);
            _txtZ.Text  = fb.Z.ToString("F2", CultureInfo.InvariantCulture);
            _txtC.Text  = fb.C.ToString("F2", CultureInfo.InvariantCulture);

            _txtJ1.Text = fb.J1.ToString("F2", CultureInfo.InvariantCulture);
            _txtJ2.Text = fb.J2.ToString("F2", CultureInfo.InvariantCulture);
            _txtJ3.Text = fb.J3.ToString("F2", CultureInfo.InvariantCulture);
            _txtJ4.Text = fb.J4.ToString("F2", CultureInfo.InvariantCulture);

            AppendLog($"[INFO] Đã nạp tọa độ thực tế: X={fb.X:F2}, Y={fb.Y:F2}, Z={fb.Z:F2}, C={fb.C:F2}");
        }

        void OnConnect()
        {
            if (!int.TryParse(_txtPort.Text.Trim(), out int port))
            { AppendLog("[ERROR] Cổng kết nối không hợp lệ."); return; }
            if (_robot.Connect(_txtIP.Text.Trim(), port))
            {
                _feedbackTimer.Start();
                Task.Delay(600).ContinueWith(_ =>
                {
                    if (InvokeRequired) Invoke((Action)CopyFeedbackToInputs);
                    else CopyFeedbackToInputs();
                });
            }
        }

        async Task OnMove()
        {
            int speed = _tbSpeed.Value;
            bool cart = _isCartesianMode;

            await Task.Run(() =>
            {
                if (cart)
                {
                    if (!Num(_txtX, "X", out double x)) return;
                    if (!Num(_txtY, "Y", out double y)) return;
                    if (!Num(_txtZ, "Z", out double z)) return;
                    if (!Num(_txtC, "C", out double c)) return;
                    _robot.MoveCartesian(x, y, z, c, speed);
                }
                else
                {
                    if (!Num(_txtJ1, "J1", out double j1)) return;
                    if (!Num(_txtJ2, "J2", out double j2)) return;
                    if (!Num(_txtJ3, "J3", out double j3)) return;
                    if (!Num(_txtJ4, "J4", out double j4)) return;
                    _robot.MoveJoint(j1, j2, j3, j4, speed);
                }
            });
        }

        bool Num(TextBox tb, string name, out double val)
        {
            if (double.TryParse(tb.Text.Trim().Replace(',', '.'),
                NumberStyles.Float, CultureInfo.InvariantCulture, out val)) return true;
            AppendLog($"[ERROR] '{name}' không phải là số hợp lệ."); return false;
        }

        void UpdateStatus()
        {
            SetStatus(_lblConnDot,  _lblConnText,
                      _robot.IsConnected, "Connected",   "Disconnected", Green, Red);
            SetStatus(_lblServoDot, _lblServoText,
                      _robot.IsServoOn,   "Servo ON",    "Servo OFF",    Green, Orange);
            SetStatus(_lblWatchDot, _lblWatchText,
                      _robot.IsWatchdog,  "Watchdog ON", "Watchdog OFF", Green, TextMute);

            _btnConnect.Enabled    = !_robot.IsConnected;
            _btnDisconnect.Enabled =  _robot.IsConnected;
            _btnServoOn.Enabled    =  _robot.IsConnected && !_robot.IsServoOn;
            _btnServoOff.Enabled   =  _robot.IsConnected &&  _robot.IsServoOn;
            _btnWatchdog.Enabled   =  _robot.IsConnected && !_robot.IsWatchdog;
            _btnMove.Enabled       =  _robot.IsConnected &&  _robot.IsServoOn && _robot.IsWatchdog;

            if (_btnDrawSquare != null)
            {
                bool canPlot = _robot.IsConnected && _robot.IsServoOn && _robot.IsWatchdog && !_isDrawing && !_isRunningSeq;
                _btnDrawSquare.Enabled    = canPlot;
                _btnDrawTriangle.Enabled  = canPlot;
                _btnDrawX.Enabled         = canPlot;
                _btnDrawCircle.Enabled    = canPlot;
                _btnPlotPenDown.Enabled   = canPlot;
                _btnPlotPenUp.Enabled     = canPlot;
                _btnPlotZInc1.Enabled     = canPlot;
                _btnPlotZDec1.Enabled     = canPlot;
                _btnPlotZInc05.Enabled    = canPlot;
                _btnPlotZDec05.Enabled    = canPlot;
                _btnPlotSetCenter.Enabled = _robot.IsConnected;
                _btnPlotSetZDraw.Enabled  = _robot.IsConnected;
                _btnAbortDraw.Enabled     = _robot.IsConnected;
            }
            if (_btnSeqRun != null)
            {
                bool canSeq = _robot.IsConnected && _robot.IsServoOn && _robot.IsWatchdog && !_isDrawing && !_isRunningSeq;
                _btnSeqRun.Enabled       = canSeq;
                _btnSeqAddPoint.Enabled  = !_isRunningSeq;
                _btnSeqDeletePoint.Enabled = !_isRunningSeq;
                _btnSeqMoveUp.Enabled    = !_isRunningSeq;
                _btnSeqMoveDown.Enabled  = !_isRunningSeq;
                _btnSeqTeach.Enabled     = _robot.IsConnected && !_isRunningSeq;
                _btnSeqSave.Enabled      = !_isRunningSeq;
                _btnSeqLoad.Enabled      = !_isRunningSeq;
                _btnSeqStop.Enabled      = _isRunningSeq;
            }
        }

        void RefreshFeedback()
        {
            var fb = _robot.GetFeedback();
            if (fb == null)
            {
                _fbX.Text = "X : —"; _fbY.Text = "Y : —"; _fbZ.Text = "Z : —"; _fbC.Text = "C : —";
                _fbJ1.Text = "J1: —"; _fbJ2.Text = "J2: —"; _fbJ3.Text = "J3: —"; _fbJ4.Text = "J4: —";
                return;
            }
            _fbX.Text  = $"X : {fb.X:F1} mm";
            _fbY.Text  = $"Y : {fb.Y:F1} mm";
            _fbZ.Text  = $"Z : {fb.Z:F1} mm";
            _fbC.Text  = $"C : {fb.C:F1}°";
            _fbJ1.Text = $"J1: {fb.J1:F1}°";
            _fbJ2.Text = $"J2: {fb.J2:F1}°";
            _fbJ3.Text = $"J3: {fb.J3:F1} mm";
            _fbJ4.Text = $"J4: {fb.J4:F1}°";
        }

        void AppendLog(string msg)
        {
            if (_log == null) return;
            if (_log.InvokeRequired) { _log.BeginInvoke((Action<string>)AppendLog, msg); return; }
            _log.AppendText($"[{DateTime.Now:HH:mm:ss}]  {msg}\n");
            _log.ScrollToCaret();
        }

        static Panel Card(string title, int w, int h)
        {
            var p = new Panel
            {
                Width = w, Height = h, BackColor = BgPanel,
                Margin = new Padding(0, 0, 0, 6),
                Padding = new Padding(6)
            };
            var l = new Label
            {
                Text = title, AutoSize = true,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = TextMain,
                Location = new Point(10, 6)
            };
            p.Controls.Add(l);
            return p;
        }

        static Label Lbl(string text, Font font = null, Color? fg = null)
            => new Label
            {
                Text = text, AutoSize = true,
                ForeColor = fg ?? TextMain,
                BackColor = Color.Transparent,
                Font = font ?? new Font("Segoe UI", 9f)
            };

        static Label FbLbl(string text)
            => new Label
            {
                Text = text, AutoSize = false, Dock = DockStyle.Fill,
                ForeColor = TextMain,
                BackColor = BgCard,
                Font = new Font("Consolas", 8.5f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(4, 0, 0, 0)
            };

        static TextBox Input(string def, int w)
            => new TextBox
            {
                Text = def, Width = w, Height = 26,
                BackColor = Color.White,
                ForeColor = TextMain,
                Font = new Font("Consolas", 9.5f),
                BorderStyle = BorderStyle.FixedSingle
            };

        static Button Btn(string text, Color bg, int w, int h)
        {
            var b = new Button
            {
                Text = text, Width = w, Height = h,
                BackColor = bg,
                ForeColor = (bg == BgPanel || bg == BgCard) ? TextMain : Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderSize = (bg == BgPanel || bg == BgCard) ? 1 : 0;
            if (b.FlatAppearance.BorderSize > 0)
                b.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
            return b;
        }

        static (Label dot, Label text) StatusRow(Panel card, string label, int y)
        {
            var dot = new Label
            {
                Text = "●", Font = new Font("Segoe UI", 9.5f),
                ForeColor = TextMute,
                AutoSize = true, Location = new Point(10, y - 2)
            };
            var txt = new Label
            {
                Text = label, AutoSize = true,
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = TextMute,
                Location = new Point(26, y)
            };
            card.Controls.Add(dot);
            card.Controls.Add(txt);
            return (dot, txt);
        }

        static void SetStatus(Label dot, Label txt, bool on,
                              string onText, string offText,
                              Color onColor, Color offColor)
        {
            dot.ForeColor = on ? onColor : offColor;
            txt.Text      = on ? onText : offText;
            txt.ForeColor = on ? onColor : TextMute;
        }

        // =================================================================
        //  PLOTTER LOGIC IMPLEMENTATION
        // =================================================================
        enum ShapeType { Square, Triangle, LetterX, Circle }

        void NudgeZ(double delta)
        {
            if (!double.TryParse(_txtPlotZDraw.Text.Trim().Replace(',', '.'),
                NumberStyles.Float, CultureInfo.InvariantCulture, out double curZ)) return;

            curZ += delta;
            if (curZ < 0) curZ = 0;
            if (curZ > 160) curZ = 160;
            _txtPlotZDraw.Text = curZ.ToString("F1", CultureInfo.InvariantCulture);

            var fb = _robot.GetFeedback();
            if (fb != null && _robot.IsConnected && _robot.IsServoOn && _robot.IsWatchdog)
            {
                Task.Run(() => _robot.MoveLinear(fb.X, fb.Y, curZ, fb.C, 5));
                AppendLog($"[PLOT] Tinh chỉnh Z = {curZ:F1} mm");
            }
        }

        async Task TestPenDown()
        {
            if (!_robot.IsConnected || !_robot.IsServoOn || !_robot.IsWatchdog)
            {
                AppendLog("[WARN] Cần kết nối, bật Watchdog và Servo trước khi thử hạ bút.");
                return;
            }
            if (!double.TryParse(_txtPlotXc.Text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double xc)) return;
            if (!double.TryParse(_txtPlotYc.Text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double yc)) return;
            if (!double.TryParse(_txtPlotZDraw.Text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double zd)) return;
            if (!double.TryParse(_txtPlotZLift.Text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double zl)) return;

            AppendLog($"[PLOT] Di chuyển tới tâm (X={xc:F1}, Y={yc:F1}) rồi hạ bút xuống Z={zd:F1}...");
            try
            {
                await Task.Run(() =>
                {
                    var fb = _robot.GetFeedback();
                    double c = fb != null ? fb.C : 0;
                    // Nhấc lên Z lift nếu đang ở thấp hơn (Z=0 là kịch sàn, Z lớn hơn là trên cao)
                    if (fb != null && fb.Z < zl)
                    {
                        _robot.MoveLinear(fb.X, fb.Y, zl, c, _tbPlotSpeed.Value);
                        _robot.WaitUntilReached(fb.X, fb.Y, zl, 2.0, 2.0, 10000);
                    }
                    // Bay tới tâm ở độ cao nhấc bút
                    _robot.MoveLinear(xc, yc, zl, c, _tbPlotSpeed.Value);
                    _robot.WaitUntilReached(xc, yc, zl, 1.5, 2.0, 15000);
                    // Hạ bút xuống Z draw và GIỮ NGUYÊN ngòi ở đó
                    _robot.MoveLinear(xc, yc, zd, c, 5);
                    _robot.WaitUntilReached(xc, yc, zd, 1.5, 2.0, 10000);
                });
                AppendLog($"[PLOT] Bút đã hạ xuống Z={zd:F1} mm. Robot đang GIỮ NGUYÊN vị trí để bạn kiểm tra ngòi chạm giấy.");
            }
            catch (Exception ex)
            {
                AppendLog($"[ERROR] Lỗi khi hạ thử bút: {ex.Message}");
                _robot.ResetMove();
            }
        }

        async Task TestPenUp()
        {
            if (!_robot.IsConnected || !_robot.IsServoOn || !_robot.IsWatchdog) return;
            if (!double.TryParse(_txtPlotZLift.Text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double zl)) return;

            AppendLog($"[PLOT] Nhấc bút lên độ cao an toàn Z={zl:F1} mm...");
            try
            {
                await Task.Run(() =>
                {
                    var fb = _robot.GetFeedback();
                    if (fb != null)
                    {
                        _robot.MoveLinear(fb.X, fb.Y, zl, fb.C, _tbPlotSpeed.Value);
                        _robot.WaitUntilReached(fb.X, fb.Y, zl, 2.0, 2.0, 10000);
                    }
                });
            }
            catch (Exception ex)
            {
                AppendLog($"[ERROR] Lỗi khi nhấc bút: {ex.Message}");
                _robot.ResetMove();
            }
        }

        async Task StartDraw(ShapeType shape)
        {
            if (!_robot.IsConnected || !_robot.IsServoOn || !_robot.IsWatchdog)
            {
                AppendLog("[WARN] Cần kết nối, bật Watchdog và Servo trước khi vẽ.");
                return;
            }

            if (!double.TryParse(_txtPlotXc.Text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double xc)) { AppendLog("[ERROR] Xc không hợp lệ."); return; }
            if (!double.TryParse(_txtPlotYc.Text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double yc)) { AppendLog("[ERROR] Yc không hợp lệ."); return; }
            if (!double.TryParse(_txtPlotZDraw.Text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double zDraw)) { AppendLog("[ERROR] Z hạ bút không hợp lệ."); return; }
            if (!double.TryParse(_txtPlotZLift.Text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double zLift)) { AppendLog("[ERROR] Z nhấc bút không hợp lệ."); return; }

            int speed = _tbPlotSpeed.Value;
            bool dryRun = _chkDryRun.Checked;
            bool pause = _chkPauseOnPenDown.Checked;

            // Xây dựng danh sách nét vẽ
            var strokes = BuildStrokes(shape, xc, yc);
            if (strokes == null || strokes.Count == 0) return;

            // Kiểm tra an toàn trước khi chạy (Preflight Guard)
            foreach (var stroke in strokes)
            {
                foreach (var pt in stroke)
                {
                    double r = Math.Sqrt(pt.Item1 * pt.Item1 + pt.Item2 * pt.Item2);
                    if (r < 105.0 || r > 295.0)
                    {
                        AppendLog($"[ERROR] Điểm ({pt.Item1:F1}, {pt.Item2:F1}) có bán kính R={r:F1}mm nằm ngoài dải an toàn (105~295mm) của THL300!");
                        AppendLog("[HINT] Vui lòng giảm kích thước hình hoặc chỉnh lại tâm Xc, Yc.");
                        return;
                    }
                }
            }

            _isDrawing = true;
            _robot.ResetCancelFlag();
            _drawCts?.Cancel();
            _drawCts?.Dispose();
            _drawCts = new CancellationTokenSource();
            var token = _drawCts.Token;

            UpdateStatus();

            AppendLog($"════════════ BẮT ĐẦU VẼ: {shape.ToString().ToUpper()} (DryRun={dryRun}) ════════════");

            try
            {
                await Task.Run(() =>
                {
                    var fb = _robot.GetFeedback();
                    double c = fb != null ? fb.C : 0;

                    for (int sIdx = 0; sIdx < strokes.Count; sIdx++)
                    {
                        if (token.IsCancellationRequested) break;
                        AppendLog($"[PLOT] Đang vẽ nét {sIdx + 1}/{strokes.Count}...");

                        bool needPause = pause && (sIdx == 0);
                        if (needPause)
                        {
                            Invoke((Action)(() => _btnContinueDraw.Enabled = true));
                        }

                        bool ok = _robot.DrawStroke(strokes[sIdx], zDraw, zLift, c, speed, needPause, _continueDrawEvent, dryRun, token);
                        if (!ok)
                        {
                            AppendLog("[PLOT] Quá trình vẽ đã dừng hoặc bị hủy.");
                            break;
                        }
                    }
                }, token);
            }
            catch (OperationCanceledException)
            {
                AppendLog("[PLOT] Đã hủy tác vụ vẽ hình thành công.");
            }
            catch (Exception ex)
            {
                AppendLog($"[ERROR] Lỗi trong quá trình vẽ: {ex.Message}");
            }
            finally
            {
                _isDrawing = false;
                try { _robot.ResetMove(); } catch { }
                Action unlockAction = () =>
                {
                    _btnContinueDraw.Enabled = false;
                    UpdateStatus();
                    AppendLog("════════════ KẾT THÚC BÀI VẼ (ĐÃ MỞ KHÓA CHO HÌNH TIẾP THEO) ════════════");
                };
                if (InvokeRequired) Invoke(unlockAction);
                else unlockAction();
            }
        }

        List<List<Tuple<double, double>>> BuildStrokes(ShapeType shape, double xc, double yc)
        {
            var res = new List<List<Tuple<double, double>>>();

            switch (shape)
            {
                case ShapeType.Square:
                    {
                        if (!double.TryParse(_txtSquareSize.Text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double size) || size <= 0)
                        { AppendLog("[ERROR] Cạnh hình vuông phải > 0."); return null; }

                        double d = size / 2.0;
                        var s = new List<Tuple<double, double>>
                        {
                            Tuple.Create(xc - d, yc - d),
                            Tuple.Create(xc + d, yc - d),
                            Tuple.Create(xc + d, yc + d),
                            Tuple.Create(xc - d, yc + d),
                            Tuple.Create(xc - d, yc - d)
                        };
                        res.Add(s);
                        break;
                    }
                case ShapeType.Triangle:
                    {
                        if (!double.TryParse(_txtTriHeight.Text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double h) || h <= 0)
                        { AppendLog("[ERROR] Đường cao tam giác phải > 0."); return null; }

                        // Tam giác đều với chiều cao h, trọng tâm đặt đúng tại (xc, yc)
                        double halfBase = h / Math.Sqrt(3.0);
                        double topY = yc + (2.0 / 3.0) * h;
                        double baseY = yc - (1.0 / 3.0) * h;
                        var s = new List<Tuple<double, double>>
                        {
                            Tuple.Create(xc, topY),                         // Đỉnh trên
                            Tuple.Create(xc + halfBase, baseY),             // Đáy phải
                            Tuple.Create(xc - halfBase, baseY),             // Đáy trái
                            Tuple.Create(xc, topY)                          // Khép về đỉnh
                        };
                        res.Add(s);
                        break;
                    }
                case ShapeType.LetterX:
                    {
                        if (!double.TryParse(_txtSquareSize.Text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double size) || size <= 0)
                        { AppendLog("[ERROR] Kích thước chữ X phải > 0."); return null; }

                        double d = size / 2.0;
                        // Nét chéo 1: góc trên-trái xuống dưới-phải
                        var s1 = new List<Tuple<double, double>>
                        {
                            Tuple.Create(xc - d, yc + d),
                            Tuple.Create(xc + d, yc - d)
                        };
                        // Nét chéo 2: góc trên-phải xuống dưới-trái
                        var s2 = new List<Tuple<double, double>>
                        {
                            Tuple.Create(xc + d, yc + d),
                            Tuple.Create(xc - d, yc - d)
                        };
                        res.Add(s1);
                        res.Add(s2);
                        break;
                    }
                case ShapeType.Circle:
                    {
                        if (!double.TryParse(_txtCircleDia.Text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double dia) || dia <= 0)
                        { AppendLog("[ERROR] Đường kính hình tròn phải > 0."); return null; }

                        if (!int.TryParse(_txtCircleSegs.Text.Trim(), out int n) || n < 4)
                        {
                            n = 36;
                            _txtCircleSegs.Text = "36";
                        }
                        if (n > 360) n = 360;

                        double r = dia / 2.0;
                        var s = new List<Tuple<double, double>>();
                        for (int i = 0; i <= n; i++)
                        {
                            double ang = i * 2.0 * Math.PI / n;
                            double x = xc + r * Math.Cos(ang);
                            double y = yc + r * Math.Sin(ang);
                            s.Add(Tuple.Create(x, y));
                        }
                        res.Add(s);
                        AppendLog($"[PLOT] Hình tròn: Đường kính={dia:F1}mm, chia {n} đoạn (mỗi đoạn {360.0/n:F1}°).");
                        break;
                    }
            }
            return res;
        }
    }
}
