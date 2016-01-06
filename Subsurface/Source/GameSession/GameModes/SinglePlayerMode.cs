﻿using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Barotrauma
{
    class SinglePlayerMode : GameMode
    {
        //private const int StartCharacterAmount = 3;

        //public readonly CrewManager CrewManager;
        //public readonly HireManager hireManager;

        private GUIButton endShiftButton;

        public readonly CargoManager CargoManager;

        private ShiftSummary shiftSummary;
        
        public Map Map;

        private bool crewDead;
        private float endTimer;

        private bool savedOnStart;

        public override Mission Mission
        {
            get
            {
                return Map.SelectedConnection.Mission;
            }
        }

        public int Money
        {
            get { return GameMain.GameSession.CrewManager.Money; }
            set { GameMain.GameSession.CrewManager.Money = value; }
        }

        private CrewManager CrewManager
        {
            get { return GameMain.GameSession.CrewManager; }
        }

        public SinglePlayerMode(GameModePreset preset)
            : base(preset)
        {

            CargoManager = new CargoManager();

            endShiftButton = new GUIButton(new Rectangle(GameMain.GraphicsWidth - 220, 20, 200, 25), "End shift", Alignment.TopLeft, GUI.Style);
            endShiftButton.Font = GUI.SmallFont;
            endShiftButton.OnClicked = EndShift;

            for (int i = 0; i < 3; i++)
            {
                JobPrefab jobPrefab = null;
                switch (i)
                {
                    case 0:
                        jobPrefab = JobPrefab.List.Find(jp => jp.Name == "Captain");
                        break;
                    case 1:
                        jobPrefab = JobPrefab.List.Find(jp => jp.Name == "Engineer");
                        break;
                    case 2:
                        jobPrefab = JobPrefab.List.Find(jp => jp.Name == "Mechanic");
                        break;
                }

                CharacterInfo characterInfo =
                    new CharacterInfo(Character.HumanConfigFile, "", Gender.None, jobPrefab);
                CrewManager.characterInfos.Add(characterInfo);
            }
  
        }

        public SinglePlayerMode(XElement element)
            : this(GameModePreset.list.Find(gm => gm.Name == "Single Player"))
        {
            string mapSeed = ToolBox.GetAttributeString(element, "mapseed", "a");

            GenerateMap(mapSeed);

            Map.SetLocation(ToolBox.GetAttributeInt(element, "currentlocation", 0));

            foreach (XElement subElement in element.Elements())
            {
                if (subElement.Name.ToString().ToLower() != "crew") continue;
                
                GameMain.GameSession.CrewManager = new CrewManager(subElement);                
            }

            savedOnStart = true;
        }

        public void GenerateMap(string seed)
        {
            Map = new Map(seed, 500);
        }

        public override void Start()
        {
            CargoManager.CreateItems();

            if (!savedOnStart)
            {
                SaveUtil.SaveGame(GameMain.GameSession.SaveFile);
                savedOnStart = true;
            }

            endTimer = 5.0f;

            isRunning = true;

            CrewManager.StartShift();

            shiftSummary = new ShiftSummary(GameMain.GameSession);
        }

        public bool TryHireCharacter(HireManager hireManager, CharacterInfo characterInfo)
        {
            if (CrewManager.Money < characterInfo.Salary) return false;

            hireManager.availableCharacters.Remove(characterInfo);
            CrewManager.characterInfos.Add(characterInfo);

            CrewManager.Money -= characterInfo.Salary;

            return true;
        }

        public string GetMoney()
        {
            return "Money: " + CrewManager.Money;
        }


        public override void Draw(SpriteBatch spriteBatch)
        {
            if (!isRunning) return;

            CrewManager.Draw(spriteBatch);

            if (Submarine.Loaded.AtEndPosition)
            {
                endShiftButton.Text = "Enter " + Map.SelectedLocation.Name;                
                endShiftButton.Draw(spriteBatch);
            }
            else if (Submarine.Loaded.AtStartPosition)
            {
                endShiftButton.Text = "Enter " + Map.CurrentLocation.Name;
                endShiftButton.Draw(spriteBatch);
            }

            //chatBox.Draw(spriteBatch);
            //textBox.Draw(spriteBatch);

            //timerBar.Draw(spriteBatch);

            //if (Game1.Client == null) endShiftButton.Draw(spriteBatch);
        }

        public override void Update(float deltaTime)
        {
            if (!isRunning) return;

            base.Update(deltaTime);

            CrewManager.Update(deltaTime);

            endShiftButton.Update(deltaTime);

            if (!crewDead)
            {
                if (!CrewManager.characters.Any(c => !c.IsDead)) crewDead = true;                
            }
            else
            {
                endTimer -= deltaTime;

                if (endTimer <= 0.0f) EndShift(null, null);
            }  
        }

        public override void End(string endMessage = "")
        {

            isRunning = false;

            //if (endMessage != "" || this.endMessage == null) this.endMessage = endMessage;

            GUIFrame summaryFrame = shiftSummary.CreateSummaryFrame();
            GUIMessageBox.MessageBoxes.Enqueue(summaryFrame);
            var okButton = new GUIButton(new Rectangle(0,0,100,30), "Ok", Alignment.BottomRight, GUI.Style, summaryFrame.children[0]);
            okButton.OnClicked = (GUIButton button, object obj) => { GUIMessageBox.MessageBoxes.Dequeue(); return true; };

            bool success = CrewManager.characters.Any(c => !c.IsDead);
            
            if (success)
            {
                if (Submarine.Loaded.AtEndPosition)
                {
                    Map.MoveToNextLocation();
                }

                SaveUtil.SaveGame(GameMain.GameSession.SaveFile);
            }
            else
            {
                var msgBox = new GUIMessageBox("Game over", "Your entire crew has died!", new string[] { "Load game", "Quit" });
                msgBox.Buttons[0].OnClicked += GameMain.GameSession.LoadPrevious;
                msgBox.Buttons[0].OnClicked += msgBox.Close;
                msgBox.Buttons[1].OnClicked = GameMain.LobbyScreen.QuitToMainMenu;
                msgBox.Buttons[1].OnClicked += msgBox.Close;
            }

            GameMain.GameSession.EndShift("");

            CrewManager.EndShift();
            for (int i = Character.CharacterList.Count - 1; i >= 0; i--)
            {
                Character.CharacterList[i].Remove();
            }

            Submarine.Unload();
        }

        private bool EndShift(GUIButton button, object obj)
        {
            isRunning = false;

            var cinematic = new TransitionCinematic(Submarine.Loaded, GameMain.GameScreen.Cam, 5.0f);

            SoundPlayer.OverrideMusicType = CrewManager.characters.Any(c => !c.IsDead) ? "endshift" : "crewdead";

            CoroutineManager.StartCoroutine(EndCinematic(cinematic));

            return true;
        }

        private IEnumerable<object> EndCinematic(TransitionCinematic cinematic)
        {
            while (cinematic.Running)
            {
                yield return CoroutineStatus.Running;
            }

            End("");

            yield return new WaitForSeconds(18.0f);
            
            SoundPlayer.OverrideMusicType = null;

            yield return CoroutineStatus.Success;
        }

        public void Save(XElement element)
        {
            //element.Add(new XAttribute("day", day));
            XElement modeElement = new XElement("gamemode");

            modeElement.Add(new XAttribute("currentlocation", Map.CurrentLocationIndex));
            modeElement.Add(new XAttribute("mapseed", Map.Seed));

            CrewManager.Save(modeElement);

            element.Add(modeElement);
            
        }
    }
}
