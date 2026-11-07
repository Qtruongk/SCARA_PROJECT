using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Test_1.Models;

namespace Test_1.UI
{
    public partial class MainForm
    {
        private TrackBar _tbTeachSpeed;
        private Label _lblTeachSpeedVal;
        private bool _syncingSpeed;
        private CancellationTokenSource _seqCts;
        private volatile bool _isRunningSeq;

        private Panel InitializePointTeachingWorkspace(
            Color primaryColor,
            Color darkText,
            Color mutedText,
            Color borderColor,
            Color canvasBg)
        {
            Panel workspaceTeaching = new Panel
            {
                Name = "workspacePointTeaching",
                Dock = DockStyle.Fill,
                BackColor = canvasBg,
                Padding = new Padding(12),
                Visible = false,
                AutoScroll = true
            };

            Panel pnlTeaching = new Panel
            {
                BackColor = Color.White,
                Location = new Point(12, 12),
                Size = new Size(760, 540)
            };
            pnlTeaching.Paint += (s, e) =>
            {
                e.Graphics.DrawRectangle(new Pen(borderColor, 1), 0, 0, pnlTeaching.Width - 1, pnlTeaching.Height - 1);
            };

            // ==========================================
            // 1. HEADER & ACTIVE TOOL TCP BADGE
            // ==========================================
            Label lblTeachTitle = new Label
            {
                Text = "📋 DẠY TỌA ĐỘ & PIPELINE GẮP WAFER (POINT TEACHING & SEQUENCER)",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = primaryColor,
                Location = new Point(14, 8),
                AutoSize = true,
                UseMnemonic = false
            };

            Label lblTeachSub = new Label
            {
                Text = "Sửa trực tiếp tọa độ, tốc độ, trễ chân không ngay trên bảng • Chạy đơn điểm hoặc chạy tự động cả dây chuyền.",
                Font = new Font("Segoe UI", 8.25F),
                ForeColor = mutedText,
                Location = new Point(14, 30),
                AutoSize = true,
                UseMnemonic = false
            };

            Label lblTeachToolBadge = new Label
            {
                Text = _robotService.IsToolActive
                    ? $"✔ {_robotService.ToolName} TCP ({_robotService.ToolOffsetX:F1}mm)"
                    : "✖ Flange (Tool 0)",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = _robotService.IsToolActive ? Color.FromArgb(46, 125, 50) : mutedText,
                Location = new Point(560, 10),
                AutoSize = true,
                UseMnemonic = false
            };

            pnlTeaching.Controls.Add(lblTeachTitle);
            pnlTeaching.Controls.Add(lblTeachSub);
            pnlTeaching.Controls.Add(lblTeachToolBadge);

            // ==========================================
            // 2. TOP INPUT, LIVE TEACH & CSV BAR
            // ==========================================
            Panel pnlTeachTopBar = new Panel
            {
                Location = new Point(14, 50),
                Size = new Size(732, 74),
                BackColor = Color.FromArgb(248, 250, 252)
            };
            pnlTeachTopBar.Paint += (s, e) =>
            {
                e.Graphics.DrawRectangle(new Pen(borderColor, 1), 0, 0, pnlTeachTopBar.Width - 1, pnlTeachTopBar.Height - 1);
            };

            Label lblNewPt = new Label
            {
                Text = "Tên:",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = darkText,
                Location = new Point(8, 12),
                AutoSize = true
            };

            TextBox txtNewPtName = new TextBox
            {
                Text = "P_WaferPick",
                Font = new Font("Segoe UI", 9F),
                Location = new Point(44, 9),
                Size = new Size(104, 23),
                BorderStyle = BorderStyle.FixedSingle
            };

            Button btnSaveCurrentPos = new Button
            {
                Text = "📍 Lưu từ Robot",
                Size = new Size(126, 27),
                BackColor = Color.FromArgb(46, 125, 50),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 8.25F, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnSaveCurrentPos.FlatAppearance.BorderSize = 0;

            Button btnSaveManualPos = new Button
            {
                Text = "➕ Thêm tọa độ",
                Size = new Size(120, 27),
                BackColor = Color.FromArgb(70, 95, 133),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 8.25F, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnSaveManualPos.FlatAppearance.BorderSize = 0;

            Button btnReadRobotToTeach = new Button
            {
                Text = "📥 Đọc Robot",
                Size = new Size(102, 27),
                BackColor = Color.FromArgb(90, 100, 115),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 8.25F, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnReadRobotToTeach.FlatAppearance.BorderSize = 0;

            Button btnImportCsv = new Button
            {
                Text = "📂 Mở CSV",
                Size = new Size(86, 27),
                BackColor = Color.FromArgb(226, 232, 240),
                ForeColor = darkText,
                Font = new Font("Segoe UI", 8.25F, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnImportCsv.FlatAppearance.BorderColor = borderColor;

            Button btnExportCsv = new Button
            {
                Text = "💾 Xuất CSV",
                Size = new Size(90, 27),
                BackColor = Color.FromArgb(226, 232, 240),
                ForeColor = darkText,
                Font = new Font("Segoe UI", 8.25F, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnExportCsv.FlatAppearance.BorderColor = borderColor;

            // Row 2 inside top bar: X, Y, Z, C, Spd%, Delay(ms), MotionType
            Label[] lblTeachCoords = new Label[4];
            TextBox[] txtTeachCoords = new TextBox[4];
            string[] coordNames = { "X:", "Y:", "Z:", "C:" };
            string[] coordDefaults = { "180.000", "0.000", "30.000", "0.00" };

            for (int i = 0; i < 4; i++)
            {
                Label lblC = new Label
                {
                    Text = coordNames[i],
                    Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                    ForeColor = darkText,
                    AutoSize = true
                };
                TextBox txtC = new TextBox
                {
                    Text = coordDefaults[i],
                    Font = new Font("Consolas", 9F, FontStyle.Bold),
                    BackColor = Color.White,
                    ForeColor = darkText,
                    Size = new Size(i == 3 ? 58 : 64, 23),
                    TextAlign = HorizontalAlignment.Right,
                    BorderStyle = BorderStyle.FixedSingle
                };
                txtC.Enter += (s, e) => txtC.SelectAll();
                txtC.Click += (s, e) => txtC.SelectAll();

                lblTeachCoords[i] = lblC;
                txtTeachCoords[i] = txtC;
                pnlTeachTopBar.Controls.Add(lblC);
                pnlTeachTopBar.Controls.Add(txtC);
            }

            Label lblNewSpd = new Label
            {
                Text = "Spd%:",
                Font = new Font("Segoe UI", 8.25F, FontStyle.Bold),
                ForeColor = darkText,
                AutoSize = true
            };
            TextBox txtNewSpd = new TextBox
            {
                Text = "20",
                Font = new Font("Consolas", 9F, FontStyle.Bold),
                Size = new Size(40, 23),
                TextAlign = HorizontalAlignment.Right,
                BorderStyle = BorderStyle.FixedSingle
            };
            txtNewSpd.Enter += (s, e) => txtNewSpd.SelectAll();

            Label lblNewDelay = new Label
            {
                Text = "Trễ(ms):",
                Font = new Font("Segoe UI", 8.25F, FontStyle.Bold),
                ForeColor = darkText,
                AutoSize = true
            };
            TextBox txtNewDelay = new TextBox
            {
                Text = "0",
                Font = new Font("Consolas", 9F, FontStyle.Bold),
                Size = new Size(48, 23),
                TextAlign = HorizontalAlignment.Right,
                BorderStyle = BorderStyle.FixedSingle
            };
            txtNewDelay.Enter += (s, e) => txtNewDelay.SelectAll();

            Label lblNewMotion = new Label
            {
                Text = "Kiểu:",
                Font = new Font("Segoe UI", 8.25F, FontStyle.Bold),
                ForeColor = darkText,
                AutoSize = true
            };
            ComboBox cmbNewMotion = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 8.5F),
                Size = new Size(72, 23)
            };
            cmbNewMotion.Items.AddRange(new object[] { "Linear", "PTP", "Direct" });
            cmbNewMotion.SelectedIndex = 0;

            pnlTeachTopBar.Controls.Add(lblNewPt);
            pnlTeachTopBar.Controls.Add(txtNewPtName);
            pnlTeachTopBar.Controls.Add(btnSaveCurrentPos);
            pnlTeachTopBar.Controls.Add(btnSaveManualPos);
            pnlTeachTopBar.Controls.Add(btnReadRobotToTeach);
            pnlTeachTopBar.Controls.Add(btnImportCsv);
            pnlTeachTopBar.Controls.Add(btnExportCsv);
            pnlTeachTopBar.Controls.Add(lblNewSpd);
            pnlTeachTopBar.Controls.Add(txtNewSpd);
            pnlTeachTopBar.Controls.Add(lblNewDelay);
            pnlTeachTopBar.Controls.Add(txtNewDelay);
            pnlTeachTopBar.Controls.Add(lblNewMotion);
            pnlTeachTopBar.Controls.Add(cmbNewMotion);
            pnlTeaching.Controls.Add(pnlTeachTopBar);

            // ==========================================
            // 3. POINTS & PIPELINE TABLE (DATAGRIDVIEW)
            // ==========================================
            DataGridView dgvPoints = new DataGridView
            {
                Location = new Point(14, 132),
                Size = new Size(732, 292),
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                GridColor = Color.FromArgb(226, 232, 240),
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = false,
                EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                RowHeadersVisible = false,
                EnableHeadersVisualStyles = false,
                AutoGenerateColumns = false
            };
            dgvPoints.ColumnHeadersDefaultCellStyle.BackColor = primaryColor;
            dgvPoints.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            dgvPoints.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 8.25F, FontStyle.Bold);
            dgvPoints.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
            dgvPoints.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            dgvPoints.ColumnHeadersHeight = 30;
            dgvPoints.DefaultCellStyle.Font = new Font("Segoe UI", 8.75F);
            dgvPoints.DefaultCellStyle.SelectionBackColor = Color.FromArgb(219, 234, 254);
            dgvPoints.DefaultCellStyle.SelectionForeColor = Color.FromArgb(30, 58, 138);
            dgvPoints.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(248, 250, 252);
            dgvPoints.RowTemplate.Height = 27;

            dgvPoints.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "colStep",
                HeaderText = "#",
                Width = 32,
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleCenter, ForeColor = mutedText }
            });
            dgvPoints.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "colId",
                HeaderText = "ID",
                Width = 44,
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 8.5F, FontStyle.Bold) }
            });
            dgvPoints.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "colName",
                HeaderText = "Tên bước / Điểm",
                MinimumWidth = 110,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
            dgvPoints.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "colX",
                HeaderText = "X (mm)",
                Width = 72,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight, Font = new Font("Consolas", 9F) }
            });
            dgvPoints.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "colY",
                HeaderText = "Y (mm)",
                Width = 72,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight, Font = new Font("Consolas", 9F) }
            });
            dgvPoints.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "colZ",
                HeaderText = "Z (mm)",
                Width = 68,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight, Font = new Font("Consolas", 9F) }
            });
            dgvPoints.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "colC",
                HeaderText = "C (°)",
                Width = 62,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight, Font = new Font("Consolas", 9F) }
            });
            dgvPoints.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "colSpd",
                HeaderText = "Spd%",
                Width = 48,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleCenter, Font = new Font("Consolas", 9F) }
            });
            dgvPoints.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "colDelay",
                HeaderText = "Trễ (ms)",
                Width = 66,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight, Font = new Font("Consolas", 9F) }
            });
            var colMotion = new DataGridViewComboBoxColumn
            {
                Name = "colMotion",
                HeaderText = "Kiểu",
                Width = 68,
                FlatStyle = FlatStyle.Flat,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleCenter }
            };
            colMotion.Items.AddRange("Linear", "PTP", "Direct");
            dgvPoints.Columns.Add(colMotion);
            dgvPoints.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "colTool",
                HeaderText = "Tool",
                Width = 54,
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleCenter }
            });

            dgvPoints.DataError += (s, e) => { e.ThrowException = false; };
            dgvPoints.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (dgvPoints.IsCurrentCellDirty && dgvPoints.CurrentCell is DataGridViewComboBoxCell)
                {
                    dgvPoints.CommitEdit(DataGridViewDataErrorContexts.Commit);
                }
            };

            pnlTeaching.Controls.Add(dgvPoints);

            // ==========================================
            // 4. BOTTOM SEQUENCE EDIT & WAFER PIPELINE CONTROLS
            // ==========================================
            // Row A: Step order & selected point editing
            Button btnMoveUp = new Button
            {
                Text = "▲ Lên",
                Size = new Size(68, 28),
                BackColor = Color.FromArgb(241, 245, 249),
                ForeColor = darkText,
                Font = new Font("Segoe UI", 8.25F, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnMoveUp.FlatAppearance.BorderColor = borderColor;

            Button btnMoveDown = new Button
            {
                Text = "▼ Xuống",
                Size = new Size(78, 28),
                BackColor = Color.FromArgb(241, 245, 249),
                ForeColor = darkText,
                Font = new Font("Segoe UI", 8.25F, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnMoveDown.FlatAppearance.BorderColor = borderColor;

            Button btnTeachOverwriteRobot = new Button
            {
                Text = "📍 Dạy đè từ Robot",
                Size = new Size(148, 28),
                BackColor = Color.FromArgb(37, 99, 235),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 8.25F, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnTeachOverwriteRobot.FlatAppearance.BorderSize = 0;

            Button btnUpdatePoint = new Button
            {
                Text = "✏️ Ghi đè từ ô nhập",
                Size = new Size(146, 28),
                BackColor = Color.FromArgb(90, 100, 115),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 8.25F, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnUpdatePoint.FlatAppearance.BorderSize = 0;

            Button btnDeletePoint = new Button
            {
                Text = "🗑️ Xóa điểm",
                Size = new Size(116, 28),
                BackColor = Color.FromArgb(198, 40, 40),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 8.25F, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnDeletePoint.FlatAppearance.BorderSize = 0;

            // Row B: Loop option + Live Status
            CheckBox chkSeqLoop = new CheckBox
            {
                Text = "🔁 Lặp chu kỳ liên tục",
                Checked = false,
                AutoSize = true,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(217, 119, 6)
            };

            Label lblSeqStatus = new Label
            {
                Text = "● Trạng thái: Sẵn sàng",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = mutedText,
                AutoSize = true,
                UseMnemonic = false
            };

            // Row C: Pipeline Run, Single Point Run, Emergency Stop
            Button btnRunPipeline = new Button
            {
                Text = "▶ CHẠY PIPELINE WAFER (SEQ)",
                Size = new Size(280, 32),
                BackColor = Color.FromArgb(22, 163, 74),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnRunPipeline.FlatAppearance.BorderSize = 0;

            Button btnRunPoint = new Button
            {
                Text = "🚀 CHẠY TỚI ĐIỂM CHỌN",
                Size = new Size(230, 32),
                BackColor = Color.FromArgb(46, 125, 50),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 8.75F, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnRunPoint.FlatAppearance.BorderSize = 0;

            Button btnStopTeachRun = new Button
            {
                Text = "⛔ DỪNG KHẨN CẤP (STOP)",
                Size = new Size(200, 32),
                BackColor = Color.FromArgb(198, 40, 40),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 8.75F, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnStopTeachRun.FlatAppearance.BorderSize = 0;

            pnlTeaching.Controls.Add(btnMoveUp);
            pnlTeaching.Controls.Add(btnMoveDown);
            pnlTeaching.Controls.Add(btnTeachOverwriteRobot);
            pnlTeaching.Controls.Add(btnUpdatePoint);
            pnlTeaching.Controls.Add(btnDeletePoint);
            pnlTeaching.Controls.Add(chkSeqLoop);
            pnlTeaching.Controls.Add(lblSeqStatus);
            pnlTeaching.Controls.Add(btnRunPipeline);
            pnlTeaching.Controls.Add(btnRunPoint);
            pnlTeaching.Controls.Add(btnStopTeachRun);

            workspaceTeaching.Controls.Add(pnlTeaching);

            // ==========================================
            // 5. RESPONSIVE & DPI-SAFE DYNAMIC LAYOUT
            // ==========================================
            Action layoutTeachingInner = () =>
            {
                int teachW = Math.Max(732, pnlTeaching.ClientSize.Width - 28);
                int teachH = Math.Max(480, pnlTeaching.ClientSize.Height);

                lblTeachToolBadge.Location = new Point(Math.Max(lblTeachTitle.Right + 12, 14 + teachW - lblTeachToolBadge.Width), 10);

                // Top Bar
                pnlTeachTopBar.Location = new Point(14, 50);
                pnlTeachTopBar.Size = new Size(teachW, 74);

                lblNewPt.Location = new Point(8, 12);
                txtNewPtName.Location = new Point(lblNewPt.Right + 4, 9);
                btnSaveCurrentPos.Location = new Point(txtNewPtName.Right + 6, 7);
                btnSaveManualPos.Location = new Point(btnSaveCurrentPos.Right + 6, 7);
                btnReadRobotToTeach.Location = new Point(btnSaveManualPos.Right + 6, 7);

                btnExportCsv.Location = new Point(teachW - 8 - btnExportCsv.Width, 7);
                btnImportCsv.Location = new Point(btnExportCsv.Left - 6 - btnImportCsv.Width, 7);

                int cx = 8;
                for (int i = 0; i < 4; i++)
                {
                    lblTeachCoords[i].Location = new Point(cx, 45);
                    txtTeachCoords[i].Location = new Point(lblTeachCoords[i].Right + 2, 42);
                    cx = txtTeachCoords[i].Right + 8;
                }
                lblNewSpd.Location = new Point(cx + 2, 45);
                txtNewSpd.Location = new Point(lblNewSpd.Right + 2, 42);
                lblNewDelay.Location = new Point(txtNewSpd.Right + 8, 45);
                txtNewDelay.Location = new Point(lblNewDelay.Right + 2, 42);
                lblNewMotion.Location = new Point(txtNewDelay.Right + 8, 45);
                cmbNewMotion.Location = new Point(lblNewMotion.Right + 2, 42);

                // DataGridView starts right below Top Bar (no Jog bar)
                int dgvTop = 132;
                int bottomReserve = 110;
                dgvPoints.Location = new Point(14, dgvTop);
                dgvPoints.Size = new Size(teachW, Math.Max(180, teachH - dgvTop - bottomReserve));

                // Bottom Row A: Sequence & Point Edit
                int editRowY = teachH - 104;
                int gapA = 8;
                int fixedSmall = 68 + 78 + gapA * 4;
                int flexTotal = Math.Max(380, teachW - fixedSmall);
                int wOverwrite = (int)(flexTotal * 0.38);
                int wManualUpd = (int)(flexTotal * 0.36);
                int wDelete = flexTotal - wOverwrite - wManualUpd;

                btnMoveUp.Location = new Point(14, editRowY);
                btnMoveUp.Size = new Size(68, 28);
                btnMoveDown.Location = new Point(btnMoveUp.Right + gapA, editRowY);
                btnMoveDown.Size = new Size(78, 28);
                btnTeachOverwriteRobot.Location = new Point(btnMoveDown.Right + gapA, editRowY);
                btnTeachOverwriteRobot.Size = new Size(wOverwrite, 28);
                btnUpdatePoint.Location = new Point(btnTeachOverwriteRobot.Right + gapA, editRowY);
                btnUpdatePoint.Size = new Size(wManualUpd, 28);
                btnDeletePoint.Location = new Point(btnUpdatePoint.Right + gapA, editRowY);
                btnDeletePoint.Size = new Size(wDelete, 28);

                // Bottom Row B: Loop Option + Live Status
                int optRowY = teachH - 70;
                chkSeqLoop.Location = new Point(14, optRowY + 3);
                lblSeqStatus.Location = new Point(Math.Max(chkSeqLoop.Right + 14, 14 + teachW - lblSeqStatus.Width), optRowY + 4);

                // Bottom Row C: Execute Pipeline / Single Move / Force Stop
                int execRowY = teachH - 38;
                int gapC = 8;
                int wPipeline = (int)((teachW - gapC * 2) * 0.42);
                int wSingle = (int)((teachW - gapC * 2) * 0.31);
                int wStop = teachW - gapC * 2 - wPipeline - wSingle;

                btnRunPipeline.Location = new Point(14, execRowY);
                btnRunPipeline.Size = new Size(wPipeline, 31);
                btnRunPoint.Location = new Point(btnRunPipeline.Right + gapC, execRowY);
                btnRunPoint.Size = new Size(wSingle, 31);
                btnStopTeachRun.Location = new Point(btnRunPoint.Right + gapC, execRowY);
                btnStopTeachRun.Size = new Size(wStop, 31);
            };

            _robotService.ToolStateChanged += () =>
            {
                if (lblTeachToolBadge.IsDisposed) return;
                Action updateBadge = () =>
                {
                    if (_robotService.IsToolActive)
                    {
                        lblTeachToolBadge.Text = $"✔ {_robotService.ToolName} TCP ({_robotService.ToolOffsetX:F1}mm)";
                        lblTeachToolBadge.ForeColor = Color.FromArgb(46, 125, 50);
                    }
                    else
                    {
                        lblTeachToolBadge.Text = "✖ Flange (Tool 0)";
                        lblTeachToolBadge.ForeColor = mutedText;
                    }
                    layoutTeachingInner();
                };
                if (lblTeachToolBadge.InvokeRequired) lblTeachToolBadge.BeginInvoke(updateBadge);
                else updateBadge();
            };

            Action layoutWorkspaceTeaching = () =>
            {
                int pad = 12;
                int availW = workspaceTeaching.ClientSize.Width - pad * 2;
                int availH = workspaceTeaching.ClientSize.Height - pad * 2;

                pnlTeaching.Location = new Point(pad, pad);
                pnlTeaching.Size = new Size(Math.Max(760, availW), Math.Max(490, availH));
                layoutTeachingInner();
            };

            pnlTeaching.Resize += (s, e) => layoutTeachingInner();
            workspaceTeaching.Resize += (s, e) => layoutWorkspaceTeaching();
            workspaceTeaching.VisibleChanged += (s, e) =>
            {
                if (workspaceTeaching.Visible) layoutWorkspaceTeaching();
            };

            // ==========================================
            // 6. DATA BINDING, INLINE EDITING & CSV
            // ==========================================
            bool suppressGridEvents = false;

            Action<string, Color> setSeqStatus = (msg, color) =>
            {
                if (lblSeqStatus.IsDisposed) return;
                Action upd = () =>
                {
                    lblSeqStatus.Text = msg;
                    lblSeqStatus.ForeColor = color;
                    lblSeqStatus.Location = new Point(
                        Math.Max(chkSeqLoop.Right + 14, 14 + Math.Max(732, pnlTeaching.ClientSize.Width - 28) - lblSeqStatus.Width),
                        lblSeqStatus.Top);
                };
                if (lblSeqStatus.InvokeRequired) lblSeqStatus.BeginInvoke(upd);
                else upd();
            };

            Action<bool> setEditingUiEnabled = (enabled) =>
            {
                Action upd = () =>
                {
                    if (pnlTeaching.IsDisposed) return;
                    btnRunPipeline.Enabled = enabled;
                    btnRunPoint.Enabled = enabled;
                    btnSaveCurrentPos.Enabled = enabled;
                    btnSaveManualPos.Enabled = enabled;
                    btnTeachOverwriteRobot.Enabled = enabled;
                    btnUpdatePoint.Enabled = enabled;
                    btnDeletePoint.Enabled = enabled;
                    btnMoveUp.Enabled = enabled;
                    btnMoveDown.Enabled = enabled;
                    btnImportCsv.Enabled = enabled;
                    dgvPoints.ReadOnly = !enabled;
                    if (enabled)
                    {
                        dgvPoints.Columns["colStep"].ReadOnly = true;
                        dgvPoints.Columns["colId"].ReadOnly = true;
                        dgvPoints.Columns["colTool"].ReadOnly = true;
                    }
                };
                if (pnlTeaching.InvokeRequired) pnlTeaching.BeginInvoke(upd);
                else upd();
            };

            Action<TeachingPoint> populateCoordInputsFromPoint = (pt) =>
            {
                if (pt == null) return;
                txtTeachCoords[0].Text = pt.X.ToString("F3", CultureInfo.InvariantCulture);
                txtTeachCoords[1].Text = pt.Y.ToString("F3", CultureInfo.InvariantCulture);
                txtTeachCoords[2].Text = pt.Z.ToString("F3", CultureInfo.InvariantCulture);
                txtTeachCoords[3].Text = pt.C.ToString("F2", CultureInfo.InvariantCulture);
                txtNewSpd.Text = pt.Speed.ToString(CultureInfo.InvariantCulture);
                txtNewDelay.Text = pt.DelayMs.ToString(CultureInfo.InvariantCulture);
                string mt = TeachingPoint.NormalizeMotionType(pt.MotionType);
                int mIdx = cmbNewMotion.Items.IndexOf(mt);
                cmbNewMotion.SelectedIndex = mIdx >= 0 ? mIdx : 0;
            };

            Action refreshPointsTable = () =>
            {
                if (dgvPoints.IsDisposed) return;
                suppressGridEvents = true;
                try
                {
                    string selectedId = (dgvPoints.CurrentRow?.Tag as TeachingPoint)?.Id;
                    dgvPoints.Rows.Clear();
                    var all = _pointTeachingService.GetAllPoints();
                    int restoreRow = -1;
                    for (int i = 0; i < all.Count; i++)
                    {
                        var pt = all[i];
                        string mt = TeachingPoint.NormalizeMotionType(pt.MotionType);
                        int r = dgvPoints.Rows.Add(
                            (i + 1).ToString(CultureInfo.InvariantCulture),
                            pt.Id,
                            pt.Name,
                            pt.X.ToString("F3", CultureInfo.InvariantCulture),
                            pt.Y.ToString("F3", CultureInfo.InvariantCulture),
                            pt.Z.ToString("F3", CultureInfo.InvariantCulture),
                            pt.C.ToString("F2", CultureInfo.InvariantCulture),
                            pt.Speed.ToString(CultureInfo.InvariantCulture),
                            pt.DelayMs.ToString(CultureInfo.InvariantCulture),
                            mt,
                            pt.IsTcpMode ? "TCP" : "Flange"
                        );
                        dgvPoints.Rows[r].Tag = pt;
                        if (!string.IsNullOrEmpty(selectedId) && string.Equals(pt.Id, selectedId, StringComparison.OrdinalIgnoreCase))
                        {
                            restoreRow = r;
                        }
                    }

                    if (restoreRow >= 0 && restoreRow < dgvPoints.Rows.Count)
                    {
                        dgvPoints.ClearSelection();
                        dgvPoints.CurrentCell = dgvPoints.Rows[restoreRow].Cells[0];
                        dgvPoints.Rows[restoreRow].Selected = true;
                    }
                    if (dgvPoints.CurrentRow?.Tag is TeachingPoint curPt)
                    {
                        populateCoordInputsFromPoint(curPt);
                    }
                }
                finally
                {
                    suppressGridEvents = false;
                }
            };

            refreshPointsTable();
            _pointTeachingService.PointsChanged += () =>
            {
                if (dgvPoints.IsDisposed) return;
                if (dgvPoints.InvokeRequired) dgvPoints.BeginInvoke(new Action(refreshPointsTable));
                else refreshPointsTable();
            };

            dgvPoints.SelectionChanged += (s, e) =>
            {
                if (suppressGridEvents) return;
                if (dgvPoints.CurrentRow?.Tag is TeachingPoint pt)
                {
                    populateCoordInputsFromPoint(pt);
                }
            };

            // Inline editing on DataGridView (persists directly without tearing down rows)
            dgvPoints.CellEndEdit += (s, e) =>
            {
                if (suppressGridEvents || e.RowIndex < 0 || e.RowIndex >= dgvPoints.Rows.Count) return;
                var row = dgvPoints.Rows[e.RowIndex];
                var pt = row.Tag as TeachingPoint;
                if (pt == null) return;

                string colName = dgvPoints.Columns[e.ColumnIndex].Name;
                string rawVal = row.Cells[e.ColumnIndex].Value?.ToString()?.Trim() ?? "";

                switch (colName)
                {
                    case "colName":
                        if (!string.IsNullOrEmpty(rawVal)) pt.Name = rawVal;
                        row.Cells[e.ColumnIndex].Value = pt.Name;
                        break;
                    case "colX":
                        if (double.TryParse(rawVal.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double nx) && !double.IsNaN(nx) && !double.IsInfinity(nx))
                            pt.X = nx;
                        row.Cells[e.ColumnIndex].Value = pt.X.ToString("F3", CultureInfo.InvariantCulture);
                        break;
                    case "colY":
                        if (double.TryParse(rawVal.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double ny) && !double.IsNaN(ny) && !double.IsInfinity(ny))
                            pt.Y = ny;
                        row.Cells[e.ColumnIndex].Value = pt.Y.ToString("F3", CultureInfo.InvariantCulture);
                        break;
                    case "colZ":
                        if (double.TryParse(rawVal.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double nz) && !double.IsNaN(nz) && !double.IsInfinity(nz))
                            pt.Z = nz;
                        row.Cells[e.ColumnIndex].Value = pt.Z.ToString("F3", CultureInfo.InvariantCulture);
                        break;
                    case "colC":
                        if (double.TryParse(rawVal.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double nc) && !double.IsNaN(nc) && !double.IsInfinity(nc))
                            pt.C = nc;
                        row.Cells[e.ColumnIndex].Value = pt.C.ToString("F2", CultureInfo.InvariantCulture);
                        break;
                    case "colSpd":
                        if (int.TryParse(rawVal, NumberStyles.Integer, CultureInfo.InvariantCulture, out int nspd))
                            pt.Speed = Math.Max(1, Math.Min(100, nspd));
                        row.Cells[e.ColumnIndex].Value = pt.Speed.ToString(CultureInfo.InvariantCulture);
                        break;
                    case "colDelay":
                        if (int.TryParse(rawVal, NumberStyles.Integer, CultureInfo.InvariantCulture, out int ndel))
                            pt.DelayMs = Math.Max(0, ndel);
                        row.Cells[e.ColumnIndex].Value = pt.DelayMs.ToString(CultureInfo.InvariantCulture);
                        break;
                    case "colMotion":
                        pt.MotionType = TeachingPoint.NormalizeMotionType(rawVal);
                        row.Cells[e.ColumnIndex].Value = pt.MotionType;
                        break;
                }

                pt.SavedAt = DateTime.Now;
                _pointTeachingService.AddOrUpdatePoint(pt, notify: false);
                populateCoordInputsFromPoint(pt);
            };

            // ==========================================
            // 7. LIVE ROBOT TEACHING & POINT CRUD
            // ==========================================
            btnReadRobotToTeach.Click += (s, e) =>
            {
                var cur = _robotService.GetCurrentPosition();
                if (cur != null)
                {
                    txtTeachCoords[0].Text = cur.WorldPosition.X.ToString("F3", CultureInfo.InvariantCulture);
                    txtTeachCoords[1].Text = cur.WorldPosition.Y.ToString("F3", CultureInfo.InvariantCulture);
                    txtTeachCoords[2].Text = cur.WorldPosition.Z.ToString("F3", CultureInfo.InvariantCulture);
                    txtTeachCoords[3].Text = cur.WorldPosition.C.ToString("F2", CultureInfo.InvariantCulture);
                    _logger.Log($"[Point] Đã đọc tọa độ Robot hiện tại: X={cur.WorldPosition.X:F3}, Y={cur.WorldPosition.Y:F3}, Z={cur.WorldPosition.Z:F3}, C={cur.WorldPosition.C:F2}");
                }
                else
                {
                    MessageBox.Show("Chưa nhận được phản hồi tọa độ từ Robot.\nVui lòng kiểm tra kết nối Controller trước khi đọc.", "Chưa kết nối Robot", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            };

            Func<int, double> parseTeachBox = (idx) =>
            {
                double.TryParse(txtTeachCoords[idx].Text.Trim().Replace(",", "."), NumberStyles.Float, CultureInfo.InvariantCulture, out double val);
                return val;
            };

            Func<int> parseNewSpeed = () =>
            {
                if (int.TryParse(txtNewSpd.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int spd))
                    return Math.Max(1, Math.Min(100, spd));
                return 20;
            };

            Func<int> parseNewDelay = () =>
            {
                if (int.TryParse(txtNewDelay.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int del))
                    return Math.Max(0, del);
                return 0;
            };

            // Teaching a NEW point strictly from live Robot feedback (prevents silent 0,0,0,0 bug)
            btnSaveCurrentPos.Click += (s, e) =>
            {
                var cur = _robotService.GetCurrentPosition();
                if (cur == null)
                {
                    MessageBox.Show(
                        "Robot hiện chưa kết nối hoặc chưa có phản hồi tọa độ thực tế!\n\n" +
                        "• Để dạy từ Robot thật: Hãy bấm Connect (và kiểm tra trạng thái) trước.\n" +
                        "• Để thêm điểm thủ công khi offline: Hãy nhập X, Y, Z, C và bấm '➕ Thêm tọa độ'.",
                        "Chưa có tọa độ thực từ Robot",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                string nextId = _pointTeachingService.GenerateNextId();
                string name = txtNewPtName.Text.Trim();
                if (string.IsNullOrEmpty(name)) name = nextId;

                var newPt = new TeachingPoint
                {
                    Id = nextId,
                    Name = name,
                    X = cur.WorldPosition.X,
                    Y = cur.WorldPosition.Y,
                    Z = cur.WorldPosition.Z,
                    C = cur.WorldPosition.C,
                    J1 = cur.JointPosition.J1,
                    J2 = cur.JointPosition.J2,
                    J3 = cur.JointPosition.J3,
                    J4 = cur.JointPosition.J4,
                    ArmConfig = (int)cur.WorldPosition.Config,
                    Speed = parseNewSpeed(),
                    DelayMs = parseNewDelay(),
                    MotionType = TeachingPoint.NormalizeMotionType(cmbNewMotion.SelectedItem?.ToString()),
                    IsTcpMode = _robotService.IsToolActive,
                    ToolName = _robotService.ToolName,
                    ToolOffsetX = _robotService.ToolOffsetX,
                    ToolOffsetY = _robotService.ToolOffsetY,
                    ToolOffsetZ = _robotService.ToolOffsetZ,
                    ToolOffsetC = _robotService.ToolOffsetC,
                    SavedAt = DateTime.Now
                };

                _pointTeachingService.AddOrUpdatePoint(newPt);
                txtNewPtName.Text = _pointTeachingService.GenerateNextId();
            };

            // Adding a NEW point from manual X, Y, Z, C inputs (with THL300 workspace radius check)
            btnSaveManualPos.Click += (s, e) =>
            {
                double x = parseTeachBox(0);
                double y = parseTeachBox(1);
                double z = parseTeachBox(2);
                double c = parseTeachBox(3);

                if (!_robotService.CheckWorkspaceRadius(x, y, out double rMm, out double minR, out double maxR))
                {
                    var ans = MessageBox.Show(
                        $"Tọa độ nhập (X={x:F2}, Y={y:F2} → R={rMm:F1}mm) nằm ngoài dải làm việc khuyến nghị ({minR:F0}~{maxR:F0}mm).\nBạn vẫn muốn lưu điểm này?",
                        "Cảnh báo vùng làm việc SCARA",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning);
                    if (ans != DialogResult.Yes) return;
                }

                string nextId = _pointTeachingService.GenerateNextId();
                string name = txtNewPtName.Text.Trim();
                if (string.IsNullOrEmpty(name)) name = nextId;

                var newPt = new TeachingPoint
                {
                    Id = nextId,
                    Name = name,
                    X = x,
                    Y = y,
                    Z = z,
                    C = c,
                    Speed = parseNewSpeed(),
                    DelayMs = parseNewDelay(),
                    MotionType = TeachingPoint.NormalizeMotionType(cmbNewMotion.SelectedItem?.ToString()),
                    IsTcpMode = _robotService.IsToolActive,
                    ToolName = _robotService.ToolName,
                    ToolOffsetX = _robotService.ToolOffsetX,
                    ToolOffsetY = _robotService.ToolOffsetY,
                    ToolOffsetZ = _robotService.ToolOffsetZ,
                    ToolOffsetC = _robotService.ToolOffsetC,
                    SavedAt = DateTime.Now
                };

                _pointTeachingService.AddOrUpdatePoint(newPt);
                txtNewPtName.Text = _pointTeachingService.GenerateNextId();
            };

            // Overwrite selected point strictly from LIVE Robot feedback (SeqTeachCurrentPoint)
            btnTeachOverwriteRobot.Click += (s, e) =>
            {
                if (dgvPoints.CurrentRow == null || !(dgvPoints.CurrentRow.Tag is TeachingPoint pt))
                {
                    MessageBox.Show("Vui lòng chọn một bước trong bảng để dạy đè vị trí Robot.", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                var cur = _robotService.GetCurrentPosition();
                if (cur == null)
                {
                    MessageBox.Show("Chưa kết nối Robot — không thể lấy vị trí thực tế để dạy đè!", "Chưa kết nối Robot", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                pt.X = cur.WorldPosition.X;
                pt.Y = cur.WorldPosition.Y;
                pt.Z = cur.WorldPosition.Z;
                pt.C = cur.WorldPosition.C;
                pt.J1 = cur.JointPosition.J1;
                pt.J2 = cur.JointPosition.J2;
                pt.J3 = cur.JointPosition.J3;
                pt.J4 = cur.JointPosition.J4;
                pt.ArmConfig = (int)cur.WorldPosition.Config;
                pt.IsTcpMode = _robotService.IsToolActive;
                pt.ToolName = _robotService.ToolName;
                pt.ToolOffsetX = _robotService.ToolOffsetX;
                pt.ToolOffsetY = _robotService.ToolOffsetY;
                pt.ToolOffsetZ = _robotService.ToolOffsetZ;
                pt.ToolOffsetC = _robotService.ToolOffsetC;
                pt.SavedAt = DateTime.Now;

                _pointTeachingService.AddOrUpdatePoint(pt);
            };

            // Overwrite selected point from manual coordinate & parameter boxes
            btnUpdatePoint.Click += (s, e) =>
            {
                if (dgvPoints.CurrentRow == null || !(dgvPoints.CurrentRow.Tag is TeachingPoint pt))
                {
                    MessageBox.Show("Vui lòng chọn một điểm trong bảng để cập nhật từ ô nhập.", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                pt.X = parseTeachBox(0);
                pt.Y = parseTeachBox(1);
                pt.Z = parseTeachBox(2);
                pt.C = parseTeachBox(3);
                pt.Speed = parseNewSpeed();
                pt.DelayMs = parseNewDelay();
                pt.MotionType = TeachingPoint.NormalizeMotionType(cmbNewMotion.SelectedItem?.ToString());
                pt.IsTcpMode = _robotService.IsToolActive;
                pt.ToolName = _robotService.ToolName;
                pt.ToolOffsetX = _robotService.ToolOffsetX;
                pt.ToolOffsetY = _robotService.ToolOffsetY;
                pt.ToolOffsetZ = _robotService.ToolOffsetZ;
                pt.ToolOffsetC = _robotService.ToolOffsetC;
                pt.SavedAt = DateTime.Now;

                _pointTeachingService.AddOrUpdatePoint(pt);
            };

            // Reorder steps Up / Down
            Action<int> moveSelectedStep = (dir) =>
            {
                int idx = dgvPoints.CurrentRow?.Index ?? -1;
                if (idx < 0) return;
                if (_pointTeachingService.MovePointOrder(idx, dir, out int newIdx))
                {
                    if (newIdx >= 0 && newIdx < dgvPoints.Rows.Count)
                    {
                        dgvPoints.ClearSelection();
                        dgvPoints.CurrentCell = dgvPoints.Rows[newIdx].Cells[0];
                        dgvPoints.Rows[newIdx].Selected = true;
                    }
                }
            };
            btnMoveUp.Click += (s, e) => moveSelectedStep(-1);
            btnMoveDown.Click += (s, e) => moveSelectedStep(+1);

            btnDeletePoint.Click += (s, e) =>
            {
                if (dgvPoints.CurrentRow == null || !(dgvPoints.CurrentRow.Tag is TeachingPoint pt))
                {
                    MessageBox.Show("Vui lòng chọn một điểm để xóa.", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                if (MessageBox.Show($"Bạn có chắc chắn muốn xóa bước '{pt.Name}' ({pt.Id}) không?", "Xác nhận xóa", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    _pointTeachingService.RemovePoint(pt.Id);
                }
            };

            // CSV Import / Export (100% compatible with RobotControl_Simple teach_points.csv)
            btnExportCsv.Click += (s, e) =>
            {
                using (var dlg = new SaveFileDialog
                {
                    Title = "Xuất danh sách điểm dạy Wafer ra CSV",
                    Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                    DefaultExt = "csv",
                    FileName = "teach_points.csv"
                })
                {
                    if (dlg.ShowDialog() != DialogResult.OK) return;
                    try
                    {
                        int count = _pointTeachingService.ExportToCsv(dlg.FileName);
                        setSeqStatus($"✔ Đã xuất {count} điểm ra CSV", Color.FromArgb(22, 163, 74));
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Lỗi xuất CSV: " + ex.Message, "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            };

            btnImportCsv.Click += (s, e) =>
            {
                using (var dlg = new OpenFileDialog
                {
                    Title = "Mở danh sách điểm dạy từ CSV (RobotControl / Pipeline)",
                    Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*"
                })
                {
                    if (dlg.ShowDialog() != DialogResult.OK) return;
                    try
                    {
                        int count = _pointTeachingService.ImportFromCsv(
                            dlg.FileName,
                            _robotService.IsToolActive,
                            _robotService.ToolName,
                            _robotService.ToolOffsetX,
                            _robotService.ToolOffsetY,
                            _robotService.ToolOffsetZ,
                            _robotService.ToolOffsetC);

                        if (count > 0)
                        {
                            setSeqStatus($"✔ Đã nạp {count} điểm từ CSV", Color.FromArgb(22, 163, 74));
                            txtNewPtName.Text = _pointTeachingService.GenerateNextId();
                        }
                        else
                        {
                            MessageBox.Show("File CSV không có dòng tọa độ hợp lệ nào.", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Lỗi đọc file CSV: " + ex.Message, "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            };

            // ==========================================
            // 8. WAFER PIPELINE & SINGLE-POINT EXECUTION ENGINE
            // ==========================================
            Action<TeachingPoint> syncPointToolFrameOnController = (pt) =>
            {
                if (pt == null) return;
                if (pt.IsTcpMode)
                {
                    double tx = Math.Abs(pt.ToolOffsetX) > 0.001 || Math.Abs(pt.ToolOffsetY) > 0.001 ? pt.ToolOffsetX : _robotService.ToolOffsetX;
                    double ty = Math.Abs(pt.ToolOffsetX) > 0.001 || Math.Abs(pt.ToolOffsetY) > 0.001 ? pt.ToolOffsetY : _robotService.ToolOffsetY;
                    double tz = Math.Abs(pt.ToolOffsetX) > 0.001 || Math.Abs(pt.ToolOffsetZ) > 0.001 ? pt.ToolOffsetZ : _robotService.ToolOffsetZ;
                    double tc = pt.ToolOffsetC;

                    if (!_robotService.IsToolActive ||
                        Math.Abs(_robotService.ToolOffsetX - tx) > 0.05 ||
                        Math.Abs(_robotService.ToolOffsetY - ty) > 0.05 ||
                        Math.Abs(_robotService.ToolOffsetZ - tz) > 0.05)
                    {
                        _robotService.SetToolOffset(tx, ty, tz, tc, pt.ToolName);
                    }
                }
                else if (_robotService.IsToolActive)
                {
                    _robotService.DisableTool();
                }
            };

            // Step execution: move directly to point according to MotionType & Speed, then dwell DelayMs
            Func<TeachingPoint, bool, CancellationToken, bool> executeStepSync =
                (pt, applyDelay, token) =>
                {
                    if (token.IsCancellationRequested) return false;

                    syncPointToolFrameOnController(pt);

                    int spd = Math.Max(1, Math.Min(100, pt.Speed));
                    string motion = TeachingPoint.NormalizeMotionType(pt.MotionType);
                    int? prefCfg = pt.ArmConfig != 0 ? (int?)pt.ArmConfig : null;

                    bool moved = string.Equals(motion, "PTP", StringComparison.OrdinalIgnoreCase)
                        ? _robotService.MoveCartesian(pt.X, pt.Y, pt.Z, pt.C, spd, prefCfg)
                        : _robotService.MoveLinear(pt.X, pt.Y, pt.Z, pt.C, spd, prefCfg);

                    if (!moved) return false;
                    if (!_robotService.WaitUntilReached(pt.X, pt.Y, pt.Z, 1.5, 2.0, 20000, token)) return false;

                    if (token.IsCancellationRequested) return false;

                    // Dwell delay for vacuum gripper actuation / stabilization
                    if (applyDelay && pt.DelayMs > 0)
                    {
                        var sw = Stopwatch.StartNew();
                        while (sw.ElapsedMilliseconds < pt.DelayMs)
                        {
                            if (token.IsCancellationRequested) return false;
                            Thread.Sleep(40);
                        }
                    }

                    return true;
                };

            // Emergency Force Stop (cancels pipeline/single move + halts controller + unlocks MoveStop flag)
            Action forceStopAll = () =>
            {
                try { _seqCts?.Cancel(); } catch { }
                Task.Run(() =>
                {
                    _robotService.StopMotion();
                    _robotService.ResetMove();
                });
                _isRunningSeq = false;
                setEditingUiEnabled(true);
                setSeqStatus("⛔ Đã dừng khẩn cấp & mở khóa", Color.FromArgb(198, 40, 40));
                _logger.Log("[SEQ] ⛔ Đã gửi lệnh DỪNG KHẨN CẤP và ResetMove mở khóa bộ điều khiển!");
            };

            btnStopTeachRun.Click += (s, e) => forceStopAll();

            this.FormClosing += (s, e) =>
            {
                try { _seqCts?.Cancel(); } catch { }
            };

            // Single Selected Point Execution
            btnRunPoint.Click += async (s, e) =>
            {
                if (_isRunningSeq || _connectionChangePending || _statusCommandPending) return;
                if (dgvPoints.CurrentRow == null || !(dgvPoints.CurrentRow.Tag is TeachingPoint selectedPt))
                {
                    MessageBox.Show("Vui lòng chọn một điểm trong bảng để chạy.", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                if (!_robotService.State.IsConnected)
                {
                    MessageBox.Show("Robot chưa kết nối. Vui lòng bấm Connect trước.", "Chưa kết nối", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (!_robotService.State.IsServoOn)
                {
                    MessageBox.Show("Servo đang tắt. Vui lòng bật Servo ON trước khi chạy.", "Servo đang tắt", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                TeachingPoint ptSnapshot = selectedPt.Clone();

                _isRunningSeq = true;
                _seqCts?.Cancel();
                _seqCts?.Dispose();
                _seqCts = new CancellationTokenSource();
                CancellationToken token = _seqCts.Token;

                setEditingUiEnabled(false);
                LoadPointToGuideTarget(ptSnapshot, false);
                setSeqStatus($"⚙ Đang tới: {ptSnapshot.Name} ({ptSnapshot.Speed}%)", Color.FromArgb(37, 99, 235));
                _logger.Log($"[Point] Chạy tới điểm '{ptSnapshot.Name}' (X={ptSnapshot.X:F3}, Y={ptSnapshot.Y:F3}, Z={ptSnapshot.Z:F3}, C={ptSnapshot.C:F2}) [{ptSnapshot.MotionType} @ {ptSnapshot.Speed}%]...");

                try
                {
                    bool ok = await Task.Run(() => executeStepSync(ptSnapshot, false, token), token);
                    if (!token.IsCancellationRequested)
                    {
                        if (ok)
                        {
                            setSeqStatus($"✔ Đã tới: {ptSnapshot.Name}", Color.FromArgb(22, 163, 74));
                            _logger.Log($"[Point] ✔ Đã tới điểm '{ptSnapshot.Name}' thành công.");
                        }
                        else
                        {
                            setSeqStatus($"⚠ Không tới kịp / lỗi: {ptSnapshot.Name}", Color.FromArgb(217, 119, 6));
                        }
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    _logger.Log("[Point] Lỗi chạy tới điểm: " + ex.Message);
                    setSeqStatus("✖ Lỗi thực thi", Color.FromArgb(198, 40, 40));
                }
                finally
                {
                    _isRunningSeq = false;
                    setEditingUiEnabled(true);
                }
            };

            // Full Wafer Pick & Place Pipeline Sequencer (SeqRun)
            btnRunPipeline.Click += async (s, e) =>
            {
                if (_isRunningSeq || _connectionChangePending || _statusCommandPending) return;
                if (!_robotService.State.IsConnected)
                {
                    MessageBox.Show("Robot chưa kết nối. Vui lòng bấm Connect trước khi chạy Pipeline.", "Chưa kết nối", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (!_robotService.State.IsServoOn)
                {
                    MessageBox.Show("Servo đang tắt. Vui lòng bật Servo ON trước khi chạy Pipeline.", "Servo đang tắt", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var pointsSnapshot = _pointTeachingService.GetAllPoints();
                if (pointsSnapshot.Count == 0)
                {
                    MessageBox.Show("Bảng điểm đang trống! Hãy dạy ít nhất 1 điểm trước khi chạy Pipeline.", "Bảng điểm trống", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                _isRunningSeq = true;
                _seqCts?.Cancel();
                _seqCts?.Dispose();
                _seqCts = new CancellationTokenSource();
                CancellationToken token = _seqCts.Token;

                setEditingUiEnabled(false);
                int cycle = 0;

                try
                {
                    do
                    {
                        cycle++;
                        bool loopEnabled = chkSeqLoop.Checked;
                        _logger.Log($"[SEQ] ══ Bắt đầu chu kỳ Pipeline Wafer #{cycle} ({(loopEnabled ? "Lặp liên tục" : "1 chu kỳ")}) ══");

                        for (int i = 0; i < pointsSnapshot.Count; i++)
                        {
                            if (token.IsCancellationRequested) break;
                            TeachingPoint pt = pointsSnapshot[i].Clone();

                            int rowIdx = i;
                            if (!dgvPoints.IsDisposed)
                            {
                                Action highlightRow = () =>
                                {
                                    if (dgvPoints.IsDisposed || rowIdx >= dgvPoints.Rows.Count) return;
                                    suppressGridEvents = true;
                                    try
                                    {
                                        dgvPoints.ClearSelection();
                                        dgvPoints.Rows[rowIdx].Selected = true;
                                        dgvPoints.FirstDisplayedScrollingRowIndex = Math.Max(0, rowIdx - 2);
                                    }
                                    catch { }
                                    finally { suppressGridEvents = false; }
                                };
                                if (dgvPoints.InvokeRequired) dgvPoints.BeginInvoke(highlightRow);
                                else highlightRow();
                            }

                            setSeqStatus($"⚙ Bước {i + 1}/{pointsSnapshot.Count}: {pt.Name}", Color.FromArgb(22, 163, 74));
                            _logger.Log($"[SEQ] → Bước {i + 1}/{pointsSnapshot.Count}: '{pt.Name}' (X={pt.X:F3}, Y={pt.Y:F3}, Z={pt.Z:F3}, C={pt.C:F2}) [{pt.MotionType}, {pt.Speed}%, Delay={pt.DelayMs}ms]");

                            bool stepOk = await Task.Run(() => executeStepSync(pt, true, token), token);
                            if (!stepOk || token.IsCancellationRequested)
                            {
                                if (!token.IsCancellationRequested)
                                {
                                    _logger.Log($"[SEQ] ⚠ Dừng dây chuyền tại bước {i + 1} ('{pt.Name}') do không đạt vị trí đích hoặc lỗi lệnh.");
                                    setSeqStatus($"⚠ Dừng tại bước {i + 1}: {pt.Name}", Color.FromArgb(217, 119, 6));
                                }
                                return;
                            }
                        }

                    } while (chkSeqLoop.Checked && !token.IsCancellationRequested);

                    if (!token.IsCancellationRequested)
                    {
                        setSeqStatus($"✔ Hoàn tất Pipeline ({cycle} chu kỳ)", Color.FromArgb(22, 163, 74));
                        _logger.Log($"[SEQ] ✔ Đã hoàn thành toàn bộ Pipeline Wafer ({cycle} chu kỳ) an toàn!");
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.Log("[SEQ] Đã hủy chu trình Pipeline.");
                }
                catch (Exception ex)
                {
                    _logger.Log("[SEQ] Lỗi thực thi Pipeline: " + ex.Message);
                    setSeqStatus("✖ Lỗi Pipeline", Color.FromArgb(198, 40, 40));
                }
                finally
                {
                    _isRunningSeq = false;
                    try { Task.Run(() => _robotService.ResetMove()); } catch { }
                    setEditingUiEnabled(true);
                }
            };

            layoutTeachingInner();
            return workspaceTeaching;
        }
    }
}
