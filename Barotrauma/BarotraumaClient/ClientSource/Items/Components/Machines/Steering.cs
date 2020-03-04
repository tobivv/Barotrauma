﻿using Barotrauma.Networking;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Xml.Linq;
using Barotrauma.Extensions;

namespace Barotrauma.Items.Components
{
    partial class Steering : Powered, IServerSerializable, IClientSerializable
    {
        private GUIButton steeringModeSwitch;
        private GUITickBox autopilotIndicator, manualPilotIndicator;

        enum Destination
        {
            MaintainPos,
            LevelEnd,
            LevelStart
        };
        private GUITickBox maintainPosTickBox, levelEndTickBox, levelStartTickBox;

        private GUIComponent statusContainer, dockingContainer, controlContainer;

        private bool dockingNetworkMessagePending;

        private GUIButton dockingButton;
        private string dockText, undockText;

        private GUIComponent steerArea;

        private GUITextBlock pressureWarningText;

        private GUITextBlock tipContainer;

        private string noPowerTip, autoPilotMaintainPosTip, autoPilotLevelStartTip, autoPilotLevelEndTip;

        private Sprite maintainPosIndicator, maintainPosOriginIndicator;
        private Sprite steeringIndicator;

        private List<DockingPort> connectedPorts = new List<DockingPort>();
        private float checkConnectedPortsTimer;
        private const float CheckConnectedPortsInterval = 1.0f;

        private Vector2 keyboardInput = Vector2.Zero;
        private float inputCumulation;

        private bool? swapDestinationOrder;

        private bool levelStartSelected;
        public bool LevelStartSelected
        {
            get { return levelStartTickBox.Selected; }
            set { levelStartTickBox.Selected = value; }
        }

        private bool levelEndSelected;
        public bool LevelEndSelected
        {
            get { return levelEndTickBox.Selected; }
            set { levelEndTickBox.Selected = value; }
        }

        private bool maintainPos;
        public bool MaintainPos
        {
            get { return maintainPosTickBox.Selected; }
            set { maintainPosTickBox.Selected = value; }
        }

        private float steerRadius;
        public float? SteerRadius
        {
            get
            {
                return steerRadius;
            }
            set
            {
                steerRadius = value ?? (steerArea.Rect.Width / 2);
            }
        }

        partial void InitProjSpecific(XElement element)
        {
            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "steeringindicator":
                        steeringIndicator = new Sprite(subElement);
                        break;
                    case "maintainposindicator":
                        maintainPosIndicator = new Sprite(subElement);
                        break;
                    case "maintainposoriginindicator":
                        maintainPosOriginIndicator = new Sprite(subElement);
                        break;
                }
            }
            CreateGUI();
            GameMain.Instance.OnResolutionChanged += RecreateGUI;
        }

        private void CreateGUI()
        {
            controlContainer = new GUIFrame(new RectTransform(new Vector2(Sonar.controlBoxSize.X, 1 - Sonar.controlBoxSize.Y * 2), GuiFrame.RectTransform, Anchor.CenterLeft), "ItemUI");
            var paddedControlContainer = new GUIFrame(new RectTransform(controlContainer.Rect.Size - GUIStyle.ItemFrameMargin, controlContainer.RectTransform, Anchor.Center)
            {
                AbsoluteOffset = GUIStyle.ItemFrameOffset
            }, style: null);

            var steeringModeArea = new GUIFrame(new RectTransform(new Vector2(1, 0.4f), paddedControlContainer.RectTransform, Anchor.TopLeft), style: null);
            steeringModeSwitch = new GUIButton(new RectTransform(new Vector2(0.2f, 1), steeringModeArea.RectTransform), string.Empty, style: "SwitchVertical")
            {
                Selected = autoPilot,
                Enabled = true,
                OnClicked = (button, data) =>
                {
                    button.Selected = !button.Selected;
                    AutoPilot = button.Selected;
                    if (GameMain.Client != null)
                    {
                        unsentChanges = true;
                        user = Character.Controlled;
                    }
                    return true;
                }
            };
            var steeringModeRightSide = new GUIFrame(new RectTransform(new Vector2(1.0f - steeringModeSwitch.RectTransform.RelativeSize.X, 0.8f), steeringModeArea.RectTransform, Anchor.CenterLeft)
            {
                RelativeOffset = new Vector2(steeringModeSwitch.RectTransform.RelativeSize.X, 0)
            }, style: null);
            manualPilotIndicator = new GUITickBox(new RectTransform(new Vector2(1, 0.45f), steeringModeRightSide.RectTransform, Anchor.TopLeft),
                TextManager.Get("SteeringManual"), font: GUI.SubHeadingFont, style: "IndicatorLightRedSmall")
            {
                Selected = !autoPilot,
                Enabled = false
            };
            autopilotIndicator = new GUITickBox(new RectTransform(new Vector2(1, 0.45f), steeringModeRightSide.RectTransform, Anchor.BottomLeft),
                TextManager.Get("SteeringAutoPilot"), font: GUI.SubHeadingFont, style: "IndicatorLightRedSmall")
            {
                Selected = autoPilot,
                Enabled = false
            };
            manualPilotIndicator.TextBlock.OverrideTextColor(GUI.Style.TextColor);
            autopilotIndicator.TextBlock.OverrideTextColor(GUI.Style.TextColor);
            GUITextBlock.AutoScaleAndNormalize(manualPilotIndicator.TextBlock, autopilotIndicator.TextBlock);

            var autoPilotControls = new GUIFrame(new RectTransform(new Vector2(0.75f, 0.62f), paddedControlContainer.RectTransform, Anchor.BottomCenter), "OutlineFrame");
            var paddedAutoPilotControls = new GUIFrame(new RectTransform(new Vector2(0.92f, 0.88f), autoPilotControls.RectTransform, Anchor.Center), style: null);

            maintainPosTickBox = new GUITickBox(new RectTransform(new Vector2(1, 0.333f), paddedAutoPilotControls.RectTransform, Anchor.TopCenter),
                TextManager.Get("SteeringMaintainPos"), font: GUI.SmallFont, style: "GUIRadioButton")
            {
                Enabled = autoPilot,
                Selected = maintainPos,
                OnSelected = tickBox =>
                {
                    if (maintainPos != tickBox.Selected)
                    {
                        unsentChanges = true;
                        user = Character.Controlled;
                        maintainPos = tickBox.Selected;
                        if (maintainPos)
                        {
                            if (controlledSub == null)
                            {
                                posToMaintain = null;
                            }
                            else
                            {
                                posToMaintain = controlledSub.WorldPosition;
                            }
                        }
                        else if (!LevelEndSelected && !LevelStartSelected)
                        {
                            AutoPilot = false;
                        }
                        if (!maintainPos)
                        {
                            posToMaintain = null;
                        }
                    }
                    return true;
                }
            };
            int textLimit = (int)(MathHelper.Clamp(25 * GUI.xScale, 15, 35));
            levelStartTickBox = new GUITickBox(new RectTransform(new Vector2(1, 0.333f), paddedAutoPilotControls.RectTransform, Anchor.Center),
                GameMain.GameSession?.StartLocation == null ? "" : ToolBox.LimitString(GameMain.GameSession.StartLocation.Name, textLimit),
                font: GUI.SmallFont, style: "GUIRadioButton")
            {
                Enabled = autoPilot,
                Selected = levelStartSelected,
                OnSelected = tickBox =>
                {
                    if (levelStartSelected != tickBox.Selected)
                    {
                        unsentChanges = true;
                        user = Character.Controlled;
                        levelStartSelected = tickBox.Selected;
                        levelEndSelected = !levelStartSelected;
                        if (levelStartSelected)
                        {
                            UpdatePath();
                        }
                        else if (!MaintainPos && !LevelEndSelected)
                        {
                            AutoPilot = false;
                        }
                    }
                    return true;
                }
            };

            levelEndTickBox = new GUITickBox(new RectTransform(new Vector2(1, 0.333f), paddedAutoPilotControls.RectTransform, Anchor.BottomCenter),
                GameMain.GameSession?.EndLocation == null ? "" : ToolBox.LimitString(GameMain.GameSession.EndLocation.Name, textLimit),
                font: GUI.SmallFont, style: "GUIRadioButton")
            {
                Enabled = autoPilot,
                Selected = levelEndSelected,
                OnSelected = tickBox =>
                {
                    if (levelEndSelected != tickBox.Selected)
                    {
                        unsentChanges = true;
                        user = Character.Controlled;
                        levelEndSelected = tickBox.Selected;
                        levelStartSelected = !levelEndSelected;
                        if (levelEndSelected)
                        {
                            UpdatePath();
                        }
                        else if (!MaintainPos && !LevelStartSelected)
                        {
                            AutoPilot = false;
                        }
                    }
                    return true;
                }
            };
            maintainPosTickBox.RectTransform.IsFixedSize = levelStartTickBox.RectTransform.IsFixedSize = levelEndTickBox.RectTransform.IsFixedSize = false;
            maintainPosTickBox.RectTransform.MaxSize = levelStartTickBox.RectTransform.MaxSize = levelEndTickBox.RectTransform.MaxSize =
                new Point(int.MaxValue, paddedAutoPilotControls.Rect.Height / 3);
            maintainPosTickBox.RectTransform.MinSize = levelStartTickBox.RectTransform.MinSize = levelEndTickBox.RectTransform.MinSize =
                Point.Zero;

            GUITextBlock.AutoScaleAndNormalize(scaleHorizontal: false, scaleVertical: true, maintainPosTickBox.TextBlock, levelStartTickBox.TextBlock, levelEndTickBox.TextBlock);

            GUIRadioButtonGroup destinations = new GUIRadioButtonGroup();
            destinations.AddRadioButton((int)Destination.MaintainPos, maintainPosTickBox);
            destinations.AddRadioButton((int)Destination.LevelStart, levelStartTickBox);
            destinations.AddRadioButton((int)Destination.LevelEnd, levelEndTickBox);
            destinations.Selected = (int)(maintainPos ? Destination.MaintainPos :
                                          levelStartSelected ? Destination.LevelStart : Destination.LevelEnd);

            // Status ->
            statusContainer = new GUIFrame(new RectTransform(Sonar.controlBoxSize, GuiFrame.RectTransform, Anchor.BottomLeft)
            {
                RelativeOffset = Sonar.controlBoxOffset
            }, "ItemUI");
            var paddedStatusContainer = new GUIFrame(new RectTransform(statusContainer.Rect.Size - GUIStyle.ItemFrameMargin, statusContainer.RectTransform, Anchor.Center, isFixedSize: false)
            {
                AbsoluteOffset = GUIStyle.ItemFrameOffset
            }, style: null);

            var elements = GUI.CreateElements(3, new Vector2(1f, 0.333f), paddedStatusContainer.RectTransform, rt => new GUIFrame(rt, style: null), Anchor.TopCenter, relativeSpacing: 0.01f);
            List<GUIComponent> leftElements = new List<GUIComponent>(), centerElements = new List<GUIComponent>(), rightElements = new List<GUIComponent>();
            for (int i = 0; i < elements.Count; i++)
            {
                var e = elements[i];
                var group = new GUILayoutGroup(new RectTransform(Vector2.One, e.RectTransform), isHorizontal: true, childAnchor: Anchor.CenterLeft)
                {
                    RelativeSpacing = 0.01f,
                    Stretch = true
                };
                var left = new GUIFrame(new RectTransform(new Vector2(0.45f, 1), group.RectTransform), style: null);
                var center = new GUIFrame(new RectTransform(new Vector2(0.15f, 1), group.RectTransform), style: null);
                var right = new GUIFrame(new RectTransform(new Vector2(0.4f, 0.8f), group.RectTransform), style: null);
                leftElements.Add(left);
                centerElements.Add(center);
                rightElements.Add(right);
                string leftText = string.Empty, centerText = string.Empty;
                GUITextBlock.TextGetterHandler rightTextGetter = null;
                switch (i)
                {
                    case 0:
                        leftText = TextManager.Get("DescentVelocity");
                        centerText = $"({TextManager.Get("KilometersPerHour")})";
                        rightTextGetter = () =>
                        {
                            Vector2 vel = controlledSub == null ? Vector2.Zero : controlledSub.Velocity;
                            var realWorldVel = ConvertUnits.ToDisplayUnits(vel.Y * Physics.DisplayToRealWorldRatio) * 3.6f;
                            return ((int)(-realWorldVel)).ToString();
                        };
                        break;
                    case 1:
                        leftText = TextManager.Get("Velocity");
                        centerText = $"({TextManager.Get("KilometersPerHour")})";
                        rightTextGetter = () =>
                        {
                            Vector2 vel = controlledSub == null ? Vector2.Zero : controlledSub.Velocity;
                            var realWorldVel = ConvertUnits.ToDisplayUnits(vel.X * Physics.DisplayToRealWorldRatio) * 3.6f;
                            return ((int)realWorldVel).ToString();
                        };
                        break;
                    case 2:
                        leftText = TextManager.Get("Depth");
                        centerText = $"({TextManager.Get("Meter")})";
                        rightTextGetter = () =>
                        {
                            Vector2 pos = controlledSub == null ? Vector2.Zero : controlledSub.Position;
                            float realWorldDepth = Level.Loaded == null ? 0.0f : Math.Abs(pos.Y - Level.Loaded.Size.Y) * Physics.DisplayToRealWorldRatio;
                            return ((int)realWorldDepth).ToString();
                        };
                        break;
                }
                new GUITextBlock(new RectTransform(Vector2.One, left.RectTransform), leftText, font: GUI.SubHeadingFont, wrap: true, textAlignment: Alignment.CenterRight);
                new GUITextBlock(new RectTransform(Vector2.One, center.RectTransform), centerText, font: GUI.Font, textAlignment: Alignment.Center) { Padding = Vector4.Zero };
                var digitalFrame = new GUIFrame(new RectTransform(Vector2.One, right.RectTransform), style: "DigitalFrameDark");
                new GUITextBlock(new RectTransform(Vector2.One * 0.85f, digitalFrame.RectTransform, Anchor.Center), "12345", GUI.Style.TextColorDark, GUI.DigitalFont, Alignment.CenterRight)
                {
                    TextGetter = rightTextGetter
                };
            }
            GUITextBlock.AutoScaleAndNormalize(leftElements.SelectMany(e => e.GetAllChildren<GUITextBlock>()));
            GUITextBlock.AutoScaleAndNormalize(centerElements.SelectMany(e => e.GetAllChildren<GUITextBlock>()));
            GUITextBlock.AutoScaleAndNormalize(rightElements.SelectMany(e => e.GetAllChildren<GUITextBlock>()));

            //docking interface ----------------------------------------------------
            float dockingButtonSize = 1.1f;
            float elementScale = 0.6f;
            dockingContainer = new GUIFrame(new RectTransform(Sonar.controlBoxSize, GuiFrame.RectTransform, Anchor.BottomLeft, scaleBasis: ScaleBasis.Smallest)
            {
                RelativeOffset = new Vector2(Sonar.controlBoxOffset.X + 0.05f, Sonar.controlBoxOffset.Y)
            }, style: null);

            dockText = TextManager.Get("label.navterminaldock", fallBackTag: "captain.dock");
            undockText = TextManager.Get("label.navterminalundock", fallBackTag: "captain.undock");
            dockingButton = new GUIButton(new RectTransform(new Vector2(elementScale), dockingContainer.RectTransform, Anchor.Center), dockText, style: "PowerButton")
            {
                OnClicked = (btn, userdata) =>
                {
                    if (GameMain.Client == null)
                    {
                        item.SendSignal(0, "1", "toggle_docking", sender: null);
                    }
                    else
                    {
                        dockingNetworkMessagePending = true;
                        item.CreateClientEvent(this);
                    }
                    return true;
                }
            };
            dockingButton.Font = GUI.SubHeadingFont;
            dockingButton.TextBlock.RectTransform.MaxSize = new Point((int)(dockingButton.Rect.Width * 0.7f), int.MaxValue);
            dockingButton.TextBlock.AutoScaleHorizontal = true;

            var style = GUI.Style.GetComponentStyle("DockingButtonUp");
            Sprite buttonSprite = style.Sprites.FirstOrDefault().Value.FirstOrDefault()?.Sprite;
            Point buttonSize = buttonSprite != null ? buttonSprite.size.ToPoint() : new Point(149, 52);
            Point horizontalButtonSize = buttonSize.Multiply(elementScale * GUI.Scale * dockingButtonSize);
            Point verticalButtonSize = horizontalButtonSize.Flip();
            var leftButton = new GUIButton(new RectTransform(verticalButtonSize, dockingContainer.RectTransform, Anchor.CenterLeft), "", style: "DockingButtonLeft")
            {
                OnClicked = NudgeButtonClicked,
                UserData = -Vector2.UnitX
            };
            var rightButton = new GUIButton(new RectTransform(verticalButtonSize, dockingContainer.RectTransform, Anchor.CenterRight), "", style: "DockingButtonRight")
            {
                OnClicked = NudgeButtonClicked,
                UserData = Vector2.UnitX
            };
            var upButton = new GUIButton(new RectTransform(horizontalButtonSize, dockingContainer.RectTransform, Anchor.TopCenter), "", style: "DockingButtonUp")
            {
                OnClicked = NudgeButtonClicked,
                UserData = Vector2.UnitY
            };
            var downButton = new GUIButton(new RectTransform(horizontalButtonSize, dockingContainer.RectTransform, Anchor.BottomCenter), "", style: "DockingButtonDown")
            {
                OnClicked = NudgeButtonClicked,
                UserData = -Vector2.UnitY
            };

            // Sonar area
            steerArea = new GUICustomComponent(new RectTransform(Vector2.One * GUI.RelativeHorizontalAspectRatio * Sonar.sonarAreaSize, GuiFrame.RectTransform, Anchor.CenterRight, scaleBasis: ScaleBasis.Smallest),
                (spriteBatch, guiCustomComponent) => { DrawHUD(spriteBatch, guiCustomComponent.Rect); }, null);
            steerRadius = steerArea.Rect.Width / 2;

            pressureWarningText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.25f), steerArea.RectTransform, Anchor.Center, Pivot.TopCenter), 
                TextManager.Get("SteeringDepthWarning"), Color.Red, GUI.LargeFont, Alignment.Center)
            {
                Visible = false
            };
            // Tooltip/helper text
            tipContainer = new GUITextBlock(new RectTransform(new Vector2(0.5f, 0.1f), steerArea.RectTransform, Anchor.BottomCenter, Pivot.TopCenter)
                , "", font: GUI.Font, wrap: true, style: "GUIToolTip", textAlignment: Alignment.Center)
            {
                AutoScaleHorizontal = true
            };
            noPowerTip = TextManager.Get("SteeringNoPowerTip");
            autoPilotMaintainPosTip = TextManager.Get("SteeringAutoPilotMaintainPosTip");
            autoPilotLevelStartTip = TextManager.GetWithVariable("SteeringAutoPilotLocationTip", "[locationname]",
                GameMain.GameSession?.StartLocation == null ? "Start" : GameMain.GameSession.StartLocation.Name);
            autoPilotLevelEndTip = TextManager.GetWithVariable("SteeringAutoPilotLocationTip", "[locationname]",
                GameMain.GameSession?.EndLocation == null ? "End" : GameMain.GameSession.EndLocation.Name);
        }

        private void RecreateGUI()
        {
            GuiFrame.ClearChildren();
            CreateGUI();
            UpdateGUIElements();
        }

        /// <summary>
        /// Makes the sonar view CustomComponent render the steering HUD, preventing it from being drawn behing the sonar
        /// </summary>
        public void AttachToSonarHUD(GUICustomComponent sonarView)
        {
            steerArea.Visible = false;
            sonarView.OnDraw += (spriteBatch, guiCustomComponent) => { DrawHUD(spriteBatch, guiCustomComponent.Rect); };
        }

        public void DrawHUD(SpriteBatch spriteBatch, Rectangle rect)
        {
            int width = rect.Width, height = rect.Height;
            int x = rect.X;
            int y = rect.Y;

            if (Voltage < MinVoltage) { return; }

            Rectangle velRect = new Rectangle(x + 20, y + 20, width - 40, height - 40);
            Vector2 displaySubPos = (-sonar.DisplayOffset * sonar.Zoom) / sonar.Range * sonar.DisplayRadius * sonar.Zoom;
            displaySubPos.Y = -displaySubPos.Y;
            displaySubPos = displaySubPos.ClampLength(velRect.Width / 2);
            displaySubPos = steerArea.Rect.Center.ToVector2() + displaySubPos;
            
            if (!AutoPilot)
            {
                Vector2 unitSteeringInput = steeringInput / 100.0f;
                //map input from rectangle to circle
                Vector2 steeringInputPos = new Vector2(
                    steeringInput.X * (float)Math.Sqrt(1.0f - 0.5f * unitSteeringInput.Y * unitSteeringInput.Y),
                    -steeringInput.Y * (float)Math.Sqrt(1.0f - 0.5f * unitSteeringInput.X * unitSteeringInput.X));
                steeringInputPos += displaySubPos;

                if (steeringIndicator != null)
                {
                    Vector2 dir = steeringInputPos - displaySubPos;
                    float angle = (float)Math.Atan2(dir.Y, dir.X);
                    steeringIndicator.Draw(spriteBatch, displaySubPos, Color.White, origin: steeringIndicator.Origin, rotate: angle,
                        scale: new Vector2(dir.Length() / steeringIndicator.size.X, 1.0f));
                }
                else
                {
                    GUI.DrawLine(spriteBatch, displaySubPos, steeringInputPos, Color.LightGray);
                    GUI.DrawRectangle(spriteBatch, new Rectangle((int)steeringInputPos.X - 5, (int)steeringInputPos.Y - 5, 10, 10), Color.White);
                }

                if (velRect.Contains(PlayerInput.MousePosition))
                {
                    GUI.DrawRectangle(spriteBatch, new Rectangle((int)steeringInputPos.X - 4, (int)steeringInputPos.Y - 4, 8, 8), GUI.Style.Red, thickness: 2);
                }
            }
            else if (posToMaintain.HasValue && !LevelStartSelected && !LevelEndSelected)
            {
                Sonar sonar = item.GetComponent<Sonar>();
                if (sonar != null && controlledSub != null)
                {
                    Vector2 displayPosToMaintain = ((posToMaintain.Value - sonar.DisplayOffset * sonar.Zoom - controlledSub.WorldPosition)) / sonar.Range * sonar.DisplayRadius * sonar.Zoom;
                    displayPosToMaintain.Y = -displayPosToMaintain.Y;
                    displayPosToMaintain = displayPosToMaintain.ClampLength(velRect.Width / 2);
                    displayPosToMaintain = steerArea.Rect.Center.ToVector2() + displayPosToMaintain;

                    Color crosshairColor = GUI.Style.Orange * (0.5f + ((float)Math.Sin(Timing.TotalTime * 5.0f) + 1.0f) / 4.0f);
                    if (maintainPosIndicator != null)
                    {
                        maintainPosIndicator.Draw(spriteBatch, displayPosToMaintain, crosshairColor, scale: 0.5f * sonar.Zoom);
                    }
                    else
                    {
                        float crossHairSize = 8.0f;
                        GUI.DrawLine(spriteBatch, displayPosToMaintain + Vector2.UnitY * crossHairSize, displayPosToMaintain - Vector2.UnitY * crossHairSize, crosshairColor, width: 3);
                        GUI.DrawLine(spriteBatch, displayPosToMaintain + Vector2.UnitX * crossHairSize, displayPosToMaintain - Vector2.UnitX * crossHairSize, crosshairColor, width: 3);
                    }

                    if (maintainPosOriginIndicator != null)
                    {
                        maintainPosOriginIndicator.Draw(spriteBatch, displaySubPos, GUI.Style.Orange, scale: 0.5f * sonar.Zoom);
                    }
                    else
                    {
                        GUI.DrawRectangle(spriteBatch, new Rectangle((int)displaySubPos.X - 5, (int)displaySubPos.Y - 5, 10, 10), GUI.Style.Orange);
                    }
                }
            }
            
            //map velocity from rectangle to circle
            Vector2 unitTargetVel = targetVelocity / 100.0f;
            Vector2 steeringPos = new Vector2(
                targetVelocity.X * 0.9f * (float)Math.Sqrt(1.0f - 0.5f * unitTargetVel.Y * unitTargetVel.Y),
                -targetVelocity.Y * 0.9f * (float)Math.Sqrt(1.0f - 0.5f * unitTargetVel.X * unitTargetVel.X));
            steeringPos += displaySubPos;


            if (steeringIndicator != null)
            {
                Vector2 dir = steeringPos - displaySubPos;
                float angle = (float)Math.Atan2(dir.Y, dir.X);
                steeringIndicator.Draw(spriteBatch, displaySubPos, Color.Gray, origin: steeringIndicator.Origin, rotate: angle,
                    scale: new Vector2(dir.Length() / steeringIndicator.size.X, 0.7f));
            }
            else
            {
                GUI.DrawLine(spriteBatch,
                    displaySubPos,
                    steeringPos,
                    Color.CadetBlue, 0, 2);
            }           
        }

        public void DebugDrawHUD(SpriteBatch spriteBatch, Vector2 transducerCenter, float displayScale, float displayRadius, Vector2 center)
        {
            if (SteeringPath == null) return;

            Vector2 prevPos = Vector2.Zero;
            foreach (WayPoint wp in SteeringPath.Nodes)
            {
                Vector2 pos = (wp.Position - transducerCenter) * displayScale;
                if (pos.Length() > displayRadius) continue;

                pos.Y = -pos.Y;
                pos += center;

                GUI.DrawRectangle(spriteBatch, new Rectangle((int)pos.X - 3 / 2, (int)pos.Y - 3, 6, 6), (SteeringPath.CurrentNode == wp) ? Color.LightGreen : GUI.Style.Green, false);

                if (prevPos != Vector2.Zero)
                {
                    GUI.DrawLine(spriteBatch, pos, prevPos, GUI.Style.Green);
                }

                prevPos = pos;
            }

            foreach (ObstacleDebugInfo obstacle in debugDrawObstacles)
            {
                Vector2 pos1 = (obstacle.Point1 - transducerCenter) * displayScale;
                pos1.Y = -pos1.Y;
                pos1 += center;
                Vector2 pos2 = (obstacle.Point2 - transducerCenter) * displayScale;
                pos2.Y = -pos2.Y;
                pos2 += center;

                GUI.DrawLine(spriteBatch, 
                    pos1, 
                    pos2,
                    GUI.Style.Red * 0.6f, width: 3);

                if (obstacle.Intersection.HasValue)
                {
                    Vector2 intersectionPos = (obstacle.Intersection.Value - transducerCenter) *displayScale;
                    intersectionPos.Y = -intersectionPos.Y;
                    intersectionPos += center;
                    GUI.DrawRectangle(spriteBatch, intersectionPos - Vector2.One * 2, Vector2.One * 4, GUI.Style.Red);
                }

                Vector2 obstacleCenter = (pos1 + pos2) / 2;
                if (obstacle.AvoidStrength.LengthSquared() > 0.01f)
                {
                    GUI.DrawLine(spriteBatch,
                        obstacleCenter,
                        obstacleCenter + new Vector2(obstacle.AvoidStrength.X, -obstacle.AvoidStrength.Y) * 100,
                        Color.Lerp(GUI.Style.Green, GUI.Style.Orange, obstacle.Dot), width: 2);
                }
            }
        }

        public override void UpdateHUD(Character character, float deltaTime, Camera cam)
        {
            if (swapDestinationOrder == null)
            {
                swapDestinationOrder = item.Submarine != null && item.Submarine.FlippedX;
                if (swapDestinationOrder.Value)
                {
                    levelStartTickBox.RectTransform.SetAsLastChild();
                }
            }

            if (steerArea.Rect.Contains(PlayerInput.MousePosition))
            {
                if (!PlayerInput.KeyDown(InputType.Deselect) && !PlayerInput.KeyHit(InputType.Deselect))
                {
                    Character.DisableControls = true;
                }
            }

            dockingContainer.Visible = DockingModeEnabled;
            statusContainer.Visible = !DockingModeEnabled;

            if (DockingModeEnabled && ActiveDockingSource != null)
            {
                if (Math.Abs(ActiveDockingSource.Item.WorldPosition.X - DockingTarget.Item.WorldPosition.X) < ActiveDockingSource.DistanceTolerance.X &&
                    Math.Abs(ActiveDockingSource.Item.WorldPosition.Y - DockingTarget.Item.WorldPosition.Y) < ActiveDockingSource.DistanceTolerance.Y)
                {
                    dockingButton.Text = dockText;
                    if (dockingButton.FlashTimer <= 0.0f)
                    {
                        dockingButton.Flash(GUI.Style.Blue, 0.5f, useCircularFlash: true);
                        dockingButton.Pulsate(Vector2.One, Vector2.One * 1.2f, dockingButton.FlashTimer);
                    }
                }
            }
            else if (DockingSources.Any(d => d.Docked))
            {
                dockingButton.Text = undockText;
                dockingContainer.Visible = true;
                statusContainer.Visible = false;
                if (dockingButton.FlashTimer <= 0.0f)
                {
                    dockingButton.Flash(GUI.Style.Orange, useCircularFlash: true);
                    dockingButton.Pulsate(Vector2.One, Vector2.One * 1.2f, dockingButton.FlashTimer);
                }
            }
            else
            {
                dockingButton.Text = dockText;
            }

            if (Voltage < MinVoltage)
            {
                tipContainer.Visible = true;
                tipContainer.Text = noPowerTip;
                return;
            }

            tipContainer.Visible = AutoPilot;
            if (AutoPilot)
            {
                if (maintainPos)
                {
                    tipContainer.Text = autoPilotMaintainPosTip;
                }
                else if (LevelStartSelected)
                {
                    tipContainer.Text = autoPilotLevelStartTip;
                }
                else if (LevelEndSelected)
                {
                    tipContainer.Text = autoPilotLevelEndTip;
                }

                if (DockingModeEnabled && DockingTarget != null)
                {
                    posToMaintain += ConvertUnits.ToDisplayUnits(DockingTarget.Item.Submarine.Velocity) * deltaTime;
                }
            }

            pressureWarningText.Visible = item.Submarine != null && item.Submarine.AtDamageDepth && Timing.TotalTime % 1.0f < 0.5f;

            if (Vector2.DistanceSquared(PlayerInput.MousePosition, steerArea.Rect.Center.ToVector2()) < steerRadius * steerRadius)
            {
                if (PlayerInput.PrimaryMouseButtonHeld() && !CrewManager.IsCommandInterfaceOpen)
                {
                    Vector2 displaySubPos = (-sonar.DisplayOffset * sonar.Zoom) / sonar.Range * sonar.DisplayRadius * sonar.Zoom;
                    displaySubPos.Y = -displaySubPos.Y;
                    displaySubPos = steerArea.Rect.Center.ToVector2() + displaySubPos;

                    Vector2 inputPos = PlayerInput.MousePosition - displaySubPos;
                    inputPos.Y = -inputPos.Y;
                    if (AutoPilot && !LevelStartSelected && !LevelEndSelected)
                    {
                        posToMaintain = controlledSub != null ? 
                            controlledSub.WorldPosition + inputPos / sonar.DisplayRadius * sonar.Range / sonar.Zoom :
                            item.Submarine == null ? item.WorldPosition : item.Submarine.WorldPosition;
                    }
                    else
                    {
                        SteeringInput = inputPos;
                    }
                    unsentChanges = true;
                    user = Character.Controlled;
                }
            }
            if (!AutoPilot && Character.DisableControls && GUI.KeyboardDispatcher.Subscriber == null)
            {
                steeringAdjustSpeed = character == null ? 0.2f : MathHelper.Lerp(0.2f, 1.0f, character.GetSkillLevel("helm") / 100.0f);
                Vector2 input = Vector2.Zero;
                if (PlayerInput.KeyDown(InputType.Left)) { input -= Vector2.UnitX; }
                if (PlayerInput.KeyDown(InputType.Right)) { input += Vector2.UnitX; }
                if (PlayerInput.KeyDown(InputType.Up)) { input += Vector2.UnitY; }
                if (PlayerInput.KeyDown(InputType.Down)) { input -= Vector2.UnitY; }
                if (PlayerInput.KeyDown(InputType.Run))
                {
                    SteeringInput += input * deltaTime * 200;
                    inputCumulation = 0;
                    keyboardInput = Vector2.Zero;
                    unsentChanges = true;
                }
                else
                {
                    float step = deltaTime * 5;
                    if (input.Length() > 0)
                    {
                        inputCumulation += step;
                    }
                    else
                    {
                        inputCumulation -= step;
                    }
                    float maxCumulation = 1;
                    inputCumulation = MathHelper.Clamp(inputCumulation, 0, maxCumulation);
                    float length = MathHelper.Lerp(0, 0.2f, MathUtils.InverseLerp(0, maxCumulation, inputCumulation));
                    var normalizedInput = Vector2.Normalize(input);
                    if (MathUtils.IsValid(normalizedInput))
                    {
                        keyboardInput += normalizedInput * length;
                    }
                    if (keyboardInput.LengthSquared() > 0.01f)
                    {
                        SteeringInput += keyboardInput;
                        unsentChanges = true;
                        user = Character.Controlled;
                        keyboardInput *= MathHelper.Clamp(1 - step, 0, 1);
                    }
                }
            }
            else
            {
                inputCumulation = 0;
                keyboardInput = Vector2.Zero;
            }

            if (!UseAutoDocking) { return; }

            if (checkConnectedPortsTimer <= 0.0f)
            {
                Connection dockingConnection = item.Connections?.FirstOrDefault(c => c.Name == "toggle_docking");
                if (dockingConnection != null)
                {
                    connectedPorts = item.GetConnectedComponentsRecursive<DockingPort>(dockingConnection);
                }
                checkConnectedPortsTimer = CheckConnectedPortsInterval;
            }
            
            float closestDist = DockingAssistThreshold * DockingAssistThreshold;
            DockingModeEnabled = false;
            
            foreach (DockingPort sourcePort in connectedPorts)
            {
                if (sourcePort.Docked || sourcePort.Item.Submarine == null) { continue; }
                if (sourcePort.Item.Submarine != controlledSub) { continue; }

                int sourceDir = sourcePort.GetDir();

                foreach (DockingPort targetPort in DockingPort.List)
                {
                    if (targetPort.Docked || targetPort.Item.Submarine == null) { continue; }
                    if (targetPort.Item.Submarine == controlledSub || targetPort.IsHorizontal != sourcePort.IsHorizontal) { continue; }
                    if (Level.Loaded != null && targetPort.Item.Submarine.WorldPosition.Y > Level.Loaded.Size.Y) { continue; }

                    int targetDir = targetPort.GetDir();

                    if (sourceDir == targetDir) { continue; }

                    float dist = Vector2.DistanceSquared(sourcePort.Item.WorldPosition, targetPort.Item.WorldPosition);
                    if (dist < closestDist)
                    {
                        DockingModeEnabled = true;
                        ActiveDockingSource = sourcePort;
                        DockingTarget = targetPort;
                    }
                }
            }
        }

        private bool NudgeButtonClicked(GUIButton btn, object userdata)
        {
            if (!MaintainPos || !AutoPilot)
            {
                AutoPilot = true;
                posToMaintain = item.Submarine.WorldPosition;
            }
            MaintainPos = true;
            if (userdata is Vector2)
            {
                Sonar sonar = item.GetComponent<Sonar>();
                Vector2 nudgeAmount = (Vector2)userdata;
                if (sonar != null)
                {
                    nudgeAmount *= sonar == null ? 500.0f : 500.0f / sonar.Zoom;
                }
                PosToMaintain += nudgeAmount;
            }
            return true;
        }

        protected override void RemoveComponentSpecific()
        {
            base.RemoveComponentSpecific();
            maintainPosIndicator?.Remove();
            maintainPosOriginIndicator?.Remove();
            steeringIndicator?.Remove();

            GameMain.Instance.OnResolutionChanged -= RecreateGUI;
        }

        public void ClientWrite(IWriteMessage msg, object[] extraData = null)
        {
            msg.Write(AutoPilot);
            msg.Write(dockingNetworkMessagePending);
            dockingNetworkMessagePending = false;

            if (!AutoPilot)
            {
                //no need to write steering info if autopilot is controlling
                msg.Write(steeringInput.X);
                msg.Write(steeringInput.Y);
            }
            else
            {
                msg.Write(posToMaintain != null);
                if (posToMaintain != null)
                {
                    msg.Write(((Vector2)posToMaintain).X);
                    msg.Write(((Vector2)posToMaintain).Y);
                }
                else
                {
                    msg.Write(LevelStartSelected);
                }
            }
        }
        
        public void ClientRead(ServerNetObject type, IReadMessage msg, float sendingTime)
        {
            int msgStartPos = msg.BitPosition;

            bool autoPilot                  = msg.ReadBoolean();
            bool dockingButtonClicked       = msg.ReadBoolean();
            Vector2 newSteeringInput        = steeringInput;
            Vector2 newTargetVelocity       = targetVelocity;
            float newSteeringAdjustSpeed    = steeringAdjustSpeed;
            bool maintainPos                = false;
            Vector2? newPosToMaintain       = null;
            bool headingToStart             = false;

            if (dockingButtonClicked)
            {
                item.SendSignal(0, "1", "toggle_docking", sender: null);
            }

            if (autoPilot)
            {
                maintainPos = msg.ReadBoolean();
                if (maintainPos)
                {
                    newPosToMaintain = new Vector2(
                        msg.ReadSingle(),
                        msg.ReadSingle());
                }
                else
                {
                    headingToStart = msg.ReadBoolean();
                }
            }
            else
            {
                newSteeringInput = new Vector2(msg.ReadSingle(), msg.ReadSingle());
                newTargetVelocity = new Vector2(msg.ReadSingle(), msg.ReadSingle());
                newSteeringAdjustSpeed = msg.ReadSingle();
            }

            if (correctionTimer > 0.0f)
            {
                int msgLength = (int)(msg.BitPosition - msgStartPos);
                msg.BitPosition = msgStartPos;
                StartDelayedCorrection(type, msg.ExtractBits(msgLength), sendingTime);
                return;
            }

            AutoPilot = autoPilot;

            if (!AutoPilot)
            {
                SteeringInput = newSteeringInput;
                TargetVelocity = newTargetVelocity;
                steeringAdjustSpeed = newSteeringAdjustSpeed;
            }
            else
            {
                MaintainPos = newPosToMaintain != null;
                posToMaintain = newPosToMaintain;

                if (posToMaintain == null)
                {
                    LevelStartSelected = headingToStart;
                    LevelEndSelected = !headingToStart;
                    UpdatePath();
                }
                else
                {
                    LevelStartSelected = false;
                    LevelEndSelected = false;
                }
            }
        }

        private void UpdateGUIElements()
        {
            steeringModeSwitch.Selected = AutoPilot;
            autopilotIndicator.Selected = AutoPilot;
            manualPilotIndicator.Selected = !AutoPilot;
            maintainPosTickBox.Enabled = AutoPilot;
            levelEndTickBox.Enabled = AutoPilot;
            levelStartTickBox.Enabled = AutoPilot;
        }
    }
}
