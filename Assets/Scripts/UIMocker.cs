using System;
using System.Collections.Generic;
using System.Drawing;
using MinesServer.Networking.Server.Packets;
using MinesServer.Networking.Server.Packets.GUI;
using MinesServer.Networking.Server.Packets.GUI.Components;
using MinesServer.Networking.Server.Packets.GUI.Components.Containers;
using MinesServer.Networking.Server.Packets.GUI.Components.Visual;
using MinesServer.Networking.Server.Packets.GUI.Components.Input;
using MinesServer.Networking.Shared.Packets;
using UnityEngine;
using UnityEngine.UIElements;
using MinesServer.Networking.Server.Packets.Compression;

public class UIMocker : MonoBehaviour
{
    private UIDocument _doc;

    public VisualElement RunMock()
    {
        var rootElement = new DockPanelPacket
        {
            Style = new GUIStylePacket
            {
                Background = System.Drawing.Color.FromArgb(255, 66, 66, 66),
                Padding = new Margins(10, 10, 10, 10)
            },
            Children = new List<IGUIComponentPacket>
            {
                new TextPacket
                {
                    Text = "<color=white>Top 0</color>",
                    Style = new GUIStylePacket {
                        Background = System.Drawing.Color.Blue,
                        Padding = new Margins(5,5,5,5)
                    },
                    AttachedProperties = new StringPairPacket[] {
                        new("DockPanel.Dock", "Top")
                    }
                },
                new TextPacket
                {
                    Text = "<color=white>Left 1</color>",
                    Style = new GUIStylePacket {
                        Background = System.Drawing.Color.Red,
                        Padding = new Margins(5,5,5,5)
                    },
                    AttachedProperties = new StringPairPacket[] {
                        new("DockPanel.Dock", "Left")
                    }
                },
                new TextPacket
                {
                    Text = "<color=white>Bottom 2</color>",
                    Style = new GUIStylePacket {
                        Background = System.Drawing.Color.Blue,
                        Padding = new Margins(5,5,5,5)
                    },
                    AttachedProperties = new StringPairPacket[] {
                        new("DockPanel.Dock", "Bottom")
                    }
                },
                new TextPacket
                {
                    Text = "<color=white>Right 3</color>",
                    Style = new GUIStylePacket {
                        Background = System.Drawing.Color.Red,
                        Padding = new Margins(5,5,5,5)
                    },
                    AttachedProperties = new StringPairPacket[] {
                        new("DockPanel.Dock", "Right")
                    }
                },
                new TextPacket
                {
                    Text = "<color=white>Bottom 4</color>",
                    Style = new GUIStylePacket {
                        Background = System.Drawing.Color.Blue,
                        Padding = new Margins(5,5,5,5)
                    },
                    AttachedProperties = new StringPairPacket[] {
                        new("DockPanel.Dock", "Bottom")
                    }
                },
                new TextPacket
                {
                    Text = "<color=white>Top 5</color>",
                    Style = new GUIStylePacket {
                        Background = System.Drawing.Color.Blue,
                        Padding = new Margins(5,5,5,5)
                    },
                    AttachedProperties = new StringPairPacket[] {
                        new("DockPanel.Dock", "Top")
                    }
                },
                new TextPacket
                {
                    Text = "<color=white>Right 6</color>",
                    Style = new GUIStylePacket {
                        Background = System.Drawing.Color.Red,
                        Padding = new Margins(5,5,5,5)
                    },
                    AttachedProperties = new StringPairPacket[] {
                        new("DockPanel.Dock", "Right")
                    }
                },
                new GridPacket
                {
                    Columns = new byte[] { 1, 0, 1, 1 },
                    Rows = new byte[] { 1, 0, 1, 1 },
                    Children = new IGUIComponentPacket[]
                    {
                        new TextPacket {
                            Text = "(0,0)",
                            AttachedProperties = new StringPairPacket[] {
                                new("Grid.Row", "0"),
                                new("Grid.Column", "0")
                            },
                            Style = new GUIStylePacket{
                                Background = System.Drawing.Color.Yellow
                            }
                        },
                        new TextPacket {
                            Text = "Auto-Row",
                            AttachedProperties = new StringPairPacket[] {
                                new("Grid.Row", "1"),
                                new("Grid.Column", "0")
                            },
                            Style = new GUIStylePacket{
                                Background = System.Drawing.Color.CornflowerBlue,
                                Padding = new Margins(5,5,15,5)
                            }
                        },
                        new TextPacket {
                            Text = "RowSpan over auto",
                            AttachedProperties = new StringPairPacket[] {
                                new("Grid.Row", "0"),
                                new("Grid.Column", "1"),
                                new("Grid.RowSpan", "3")
                            },
                            Style = new GUIStylePacket{
                                Background = System.Drawing.Color.LimeGreen
                            }
                        },
                        new TextPacket {
                            Text = "ColSpan",
                            AttachedProperties = new StringPairPacket[] {
                                new("Grid.Row", "3"),
                                new("Grid.Column", "2"),
                                new("Grid.ColumnSpan", "2")
                            },
                            Style = new GUIStylePacket{
                                Background = System.Drawing.Color.HotPink
                            }
                        },
                    }
                }
            }
        };

        var windowPacket = new OpenWindowPacket("TestWindow", 800, 600, rootElement);

        var packet = new ServerPacket(windowPacket);
        Span<byte> span = stackalloc byte[packet.Size];
        packet.Encode(span);
        packet = ServerPacket.Decode(span);

        var builtUI = new PacketUIBuilder().Build(((OpenWindowPacket)packet.Payload).Content);
        builtUI.style.width = new Length(((OpenWindowPacket)packet.Payload).Width, LengthUnit.Pixel);
        builtUI.style.height = new Length(((OpenWindowPacket)packet.Payload).Height, LengthUnit.Pixel);
        builtUI.style.position = Position.Absolute;
        builtUI.style.left = new Length(50, LengthUnit.Percent);
        builtUI.style.top = new Length(50, LengthUnit.Percent);
        builtUI.style.translate = new Translate(new Length(-50, LengthUnit.Percent), new Length(-50, LengthUnit.Percent));
        

        Debug.Log("UI Built Successfully!");
        return builtUI;
    }

    public VisualElement RunComprehensiveMock()
    {
        var @checked = new ImagePacket()
        {
            URI = "/ui/checked.png",
            Width = 32,
            Height = 32
        };
        var @unchecked = new ImagePacket()
        {
            URI = "/ui/unchecked.png",
            Width = 32,
            Height = 32
        };
        var rootElement = new DockPanelPacket
        {
            Style = new GUIStylePacket
            {
                Background = System.Drawing.Color.FromArgb(255, 22, 22, 22),
                Padding = new Margins(5, 5, 5, 5)
            },
            Children = new List<IGUIComponentPacket>
            {
                new TextPacket
                {
                    Text = "<color=white>Header</color>",
                    Style = new GUIStylePacket {
                        Background = System.Drawing.Color.DarkBlue,
                        Padding = new Margins(5,5,5,5)
                    },
                    AttachedProperties = new StringPairPacket[] {
                        new("DockPanel.Dock", "Top")
                    }
                },
                new TextPacket
                {
                    Text = "<color=white>Footer</color>",
                    Style = new GUIStylePacket {
                        Background = System.Drawing.Color.DarkBlue,
                        Padding = new Margins(5,5,5,5)
                    },
                    AttachedProperties = new StringPairPacket[] {
                        new("DockPanel.Dock", "Bottom")
                    }
                },
                new DockPanelPacket
                {
                    Style = new GUIStylePacket
                    {
                        Background = System.Drawing.Color.FromArgb(255, 99, 99, 99),
                        Padding = new Margins(10, 10, 10, 10)
                    },
                    Children = new List<IGUIComponentPacket>
                    {
                        new TextPacket
                        {
                            Text = "<color=white>Left-Top</color>",
                            Style = new GUIStylePacket {
                                Background = System.Drawing.Color.DarkCyan,
                                Padding = new Margins(5,5,5,5)
                            },
                            AttachedProperties = new StringPairPacket[] {
                                new("DockPanel.Dock", "Top")
                            }
                        },
                        new ScrollViewerPacket
                        {
                             Children = new IGUIComponentPacket[]
                             {
                                 new SelectablePacket
                                 {
                                     Name = "testcheckbox",
                                     Checked = @checked,
                                     Unchecked = @unchecked
                                 },
                                 new SelectablePacket
                                 {
                                     Name = "testcheckbox2",
                                     Checked = @checked,
                                     Unchecked = @unchecked
                                 },
                                 new SelectablePacket
                                 {
                                     Name = "testcheckbox3",
                                     Checked = @checked,
                                     Unchecked = @unchecked
                                 },
                                 new TextBoxPacket {
                                     DefaultValue = "123123123",
                                     Name = "textbox",
                                     Regex = "^\\d*$",
                                     AttachedProperties = new StringPairPacket[] {
                                         new("DockPanel.Dock", "Top")
                                     },
                                     Style = new GUIStylePacket{
                                         Background = System.Drawing.Color.LightGray
                                     }
                                },
                                new SliderPacket {
                                    DefaultValue = 0,
                                    MinValue = 0,
                                    MaxValue = 100,
                                    Name = "slider",
                                    Knob = new()
                                    {
                                        URI = "/ui/knob.png",
                                        Width = 16,
                                        Height = 16
                                    },
                                    AttachedProperties = new StringPairPacket[] {
                                        new("DockPanel.Dock", "Top")
                                    }
                                },
                                new StringDropdownPacket {
                                    Name = "stringdropdown",
                                    Values = new[] { "asd", "sad" },
                                    AttachedProperties = new StringPairPacket[] {
                                        new("DockPanel.Dock", "Top")
                                    }
                                },
                                new IntDropdownPacket {
                                    Name = "intdropdown",
                                    Values = new[] { 1, 2, 3, 4, 5 },
                                    AttachedProperties = new StringPairPacket[] {
                                        new("DockPanel.Dock", "Top")
                                    }
                                },
                                new ImagePacket {
                                    URI = "/test.png",
                                    Width = 50,
                                    Height = 50,
                                    AttachedProperties = new StringPairPacket[] {
                                        new("DockPanel.Dock", "Top")
                                    }
                                },
                                new SelectablePacket
                                {
                                     Name = "radio",
                                     Checked = @checked,
                                     Unchecked = @unchecked
                                },
                                new SelectablePacket
                                {
                                     Name = "radio",
                                     Checked = @checked,
                                     Unchecked = @unchecked
                                },
                                new SelectablePacket
                                {
                                     Name = "radio",
                                     Checked = @checked,
                                     Unchecked = @unchecked
                                },
                             }
                        }
                    }
                }
            }
        };

        var windowPacket = new OpenWindowPacket("ComprehensiveTestWindow", 1200, 800, rootElement);

        var packet = new ServerPacket(windowPacket);
        Span<byte> span = stackalloc byte[packet.Size];
        packet.Encode(span);
        packet = ServerPacket.Decode(span);

        var builtUI = new PacketUIBuilder().Build(((OpenWindowPacket)packet.Payload).Content);
        builtUI.style.width = new Length(((OpenWindowPacket)packet.Payload).Width, LengthUnit.Pixel);
        builtUI.style.height = new Length(((OpenWindowPacket)packet.Payload).Height, LengthUnit.Pixel);
        builtUI.style.position = Position.Absolute;
        builtUI.style.left = new Length(50, LengthUnit.Percent);
        builtUI.style.top = new Length(50, LengthUnit.Percent);
        builtUI.style.translate = new Translate(new Length(-50, LengthUnit.Percent), new Length(-50, LengthUnit.Percent));
        

        Debug.Log("Complex UI Built Successfully!");
        return builtUI;
    }
}