﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;
using WargameModInstaller.Common.Entities;
using WargameModInstaller.Common.Extensions;
using WargameModInstaller.Model;
using WargameModInstaller.Model.Commands;
using WargameModInstaller.Model.Config;
using WargameModInstaller.Services.Config;

namespace WargameModInstaller.Infrastructure.Commands
{
    //To do: Make this unaware of any high level services like the ISettingsProvider.
    //There should be a some high level command providing service which would use this reader, 
    //and eventually use other services to adjust the commands values.

    /// <summary>
    /// 
    /// </summary>
    public class InstallCmdReader : WMIReaderBase<XElement, IEnumerable<IInstallCmd>>, IInstallCmdReader
    {
        private readonly String installCommandsElementPath = "WargameModInstallerConfig/InstallCommands";
        private readonly ISettingsProvider settingsProvider;
        private readonly bool defaultCriticalValue;

        public InstallCmdReader(ISettingsProvider settingsProvider)
        {
            this.settingsProvider = settingsProvider;
            this.defaultCriticalValue = settingsProvider
                .GetGeneralSettings(GeneralSettingEntryType.CriticalCommands)
                .Value
                .ToOrDefault<bool>();
            this.GroupProductionRules = CreateGroupProductionRules().OrderByDescending(x => x.Priority);
        }

        protected IEnumerable<GroupProductionRule> GroupProductionRules
        {
            get;
            private set;
        }

        /// <summary>
        /// Reads all install command entires.
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        public virtual IEnumerable<IInstallCmd> ReadAll(String filePath)
        {
            var cmds = new List<IInstallCmd>();
            try
            {
                XDocument configFile = XDocument.Load(filePath);
                XElement rootElement = configFile.XPathSelectElement(installCommandsElementPath);
                if (rootElement == null)
                {
                    return cmds;
                }

                foreach (var cmdQuery in ReadingQueries.Values)
                {
                    var queryResult = cmdQuery(rootElement);
                    cmds.AddRange(queryResult);
                }

                int id = 0;
                cmds.ForEach(cmd => cmd.Id = id++);
            }
            catch (XmlException ex)
            {
                Common.Logging.LoggerFactory.Create(this.GetType()).Error(ex);

                throw;
            }

            return cmds;
        }

        /// <summary>
        /// Reads all install command entires.
        /// </summary>
        /// <param name="filePath"></param>
        /// <param name="components"></param>
        /// <returns></returns>
        public IEnumerable<IInstallCmd> ReadAll(String filePath, IEnumerable<String> components)
        {
            var cmds = new List<IInstallCmd>();
            try
            {
                XDocument configFile = XDocument.Load(filePath);
                XElement rootElement = configFile.XPathSelectElement(installCommandsElementPath);
                if (rootElement == null)
                {
                    return cmds;
                }

                var cmdParentElements = new List<XElement>();
                var componentsNames = new HashSet<String>(components);
                foreach (var name in componentsNames)
                {
                    var element = rootElement.XPathSelectElement(String.Format("//*[@name=\"{0}\"]", name));
                    if (element != null)
                    {
                        cmdParentElements.Add(element);
                    }
                }

                foreach (var element in cmdParentElements)
                {
                    foreach (var cmdQuery in ReadingQueries.Values)
                    {
                        var queryResult = cmdQuery(element);
                        cmds.AddRange(queryResult);
                    }
                }

                int id = 0;
                cmds.ForEach(cmd => cmd.Id = id++);
            }
            catch (XmlException ex)
            {
                Common.Logging.LoggerFactory.Create(this.GetType()).Error(ex);

                throw;
            }

            return cmds;
        }

        /// <summary>
        /// Reads all install comand entries and groups them if possible.
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        public virtual IEnumerable<ICmdGroup> ReadGroups(String filePath)
        {
            var cmdGroupsList = new List<ICmdGroup>();
            var cmdsList = ReadAll(filePath).ToList();

            try
            {
                cmdGroupsList = CreateCommandGroups(cmdsList);

                cmdGroupsList.OrderByDescending(group => group.Priority);
            }
            catch (XmlException ex)
            {
                Common.Logging.LoggerFactory.Create(this.GetType()).Error(ex);

                throw;
            }

            return cmdGroupsList;
        }

        /// <summary>
        /// Reads all install comand entries and groups them if possible.
        /// </summary>
        /// <param name="filePath"></param>
        /// <param name="components"></param>
        /// <returns></returns>
        public IEnumerable<ICmdGroup> ReadGroups(String filePath, IEnumerable<String> components)
        {
            var cmdGroupsList = new List<ICmdGroup>();
            var cmdsList = ReadAll(filePath, components).ToList();

            try
            {
                cmdGroupsList = CreateCommandGroups(cmdsList);

                cmdGroupsList.OrderByDescending(group => group.Priority);
            }
            catch (XmlException ex)
            {
                WargameModInstaller.Common.Logging.LoggerFactory.Create(this.GetType()).Error(ex);

                throw;
            }

            return cmdGroupsList;
        }

        protected List<ICmdGroup> CreateCommandGroups(List<IInstallCmd> cmdsList)
        {
            var cmdGroupsList = new List<ICmdGroup>();

            foreach (var rule in GroupProductionRules)
            {
                var group = rule.ProduceGroup(cmdsList);
                foreach (var grp in group)
                {
                    cmdsList.RemoveAll(cmd => grp.Commands.Contains(cmd)); // Do zastąpienia przez hasz set
                }

                cmdGroupsList.AddRange(group);
            }

            return cmdGroupsList;
        }

        protected override Dictionary<WMIEntryType, Func<XElement, IEnumerable<IInstallCmd>>> CreateReadingQueries()
        {
            var queries = new Dictionary<WMIEntryType, Func<XElement, IEnumerable<IInstallCmd>>>();
            queries.Add(CmdEntryType.CopyGameFile, ReadCopyModFileCmds);
            queries.Add(CmdEntryType.CopyModFile, ReadCopyGameFileCmds);
            queries.Add(CmdEntryType.RemoveFile, ReadRemoveFileCmds);
            queries.Add(CmdEntryType.ReplaceImage, ReadReplaceImageCmds);
            queries.Add(CmdEntryType.ReplaceImagePart, ReadReplaceImageTileCmds);
            queries.Add(CmdEntryType.ReplaceImageTile, ReadReplaceImagePartCmds);
            queries.Add(CmdEntryType.ReplaceContent, ReadReplaceContentCmds);
            queries.Add(CmdEntryType.AlterDictionary, ReadAlterDictionaryCmds);

            return queries;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        /// <remarks>Higher priority number means higher priority rule.</remarks>
        protected virtual IEnumerable<GroupProductionRule> CreateGroupProductionRules()
        {
            var rules = new List<GroupProductionRule>();
            rules.Add(new GroupProductionRule(3, MultiLevelCmdGroupProductionRule));
            rules.Add(new GroupProductionRule(2, EdataCmdGroupProductionRule));
            rules.Add(new GroupProductionRule(1, BasicCmdGroupProductionRule));

            return rules;
        }

        private IEnumerable<ICmdGroup> BasicCmdGroupProductionRule(IEnumerable<IInstallCmd> cmds)
        {
            var resultGroups = new List<BasicCmdGroup>();

            var samePriorityGroups = cmds.GroupBy(cmd => cmd.Priority);
            foreach (var group in samePriorityGroups)
            {
                var newBasicGroup = new BasicCmdGroup(group, group.Key);
                resultGroups.Add(newBasicGroup);
            }

            return resultGroups;
        }

        private IEnumerable<ICmdGroup> EdataCmdGroupProductionRule(IEnumerable<IInstallCmd> cmds)
        {
            var edataCmds = new List<IInstallCmd>();
            edataCmds.AddRange(cmds.OfType<ReplaceImageCmd>());
            edataCmds.AddRange(cmds.OfType<ReplaceImagePartCmd>());
            edataCmds.AddRange(cmds.OfType<ReplaceImageTileCmd>());
            edataCmds.AddRange(cmds.OfType<ReplaceContentCmd>());
            edataCmds.AddRange(cmds.OfType<AlterDictionaryCmd>());

            var groups = from cmd in edataCmds
                         group cmd by new { ((IHasTarget)cmd).TargetPath, cmd.Priority };


            var resultGroups = new List<EdataCmdGroup>();
            foreach (var group in groups)
            {
                var newGroup = new EdataCmdGroup(group, group.Key.TargetPath, group.Key.Priority);
                resultGroups.Add(newGroup);
            }

            return resultGroups;
        }

        private IEnumerable<ICmdGroup> MultiLevelCmdGroupProductionRule(IEnumerable<IInstallCmd> cmds)
        {
            var multiLevelCmds = cmds
                .OfType<IHasTargetContent>()
                .Where(cmd => cmd.TargetContentPath.PathType == ContentPathType.EdataNestedContent);

            var multiLevelEdataCmds = new List<IInstallCmd>();
            multiLevelEdataCmds.AddRange(multiLevelCmds.OfType<ReplaceImageCmd>());
            multiLevelEdataCmds.AddRange(multiLevelCmds.OfType<ReplaceImagePartCmd>());
            multiLevelEdataCmds.AddRange(multiLevelCmds.OfType<ReplaceImageTileCmd>());
            multiLevelEdataCmds.AddRange(multiLevelCmds.OfType<ReplaceContentCmd>());
            multiLevelEdataCmds.AddRange(multiLevelCmds.OfType<AlterDictionaryCmd>());

            var groups = from cmd in multiLevelEdataCmds
                         group cmd by new { 
                             cmd.Priority,
                             ((IHasTarget)cmd).TargetPath, 
                             ((IHasTargetContent)cmd).TargetContentPath.PreLastPart 
                         };


            var resultGroups = new List<MultiLevelEdataCmdGroup>();
            foreach (var group in groups)
            {
                var multilevelContentPath = new ContentPath(group.Key.PreLastPart);
                var newGroup = new MultiLevelEdataCmdGroup(
                    group, 
                    group.Key.TargetPath,
                    multilevelContentPath,
                    group.Key.Priority);
                resultGroups.Add(newGroup);
            }

            return resultGroups;
        }


        private IEnumerable<CopyModFileCmd> ReadCopyModFileCmds(XElement source)
        {
            var result = new List<CopyModFileCmd>();

            var cmdElementsCollection = source.Elements(CmdEntryType.CopyModFile.Name);
            foreach (var cmdElement in cmdElementsCollection)
            {
                var sourcePath = cmdElement.Attribute("sourcePath").ValueNullSafe();
                var targetPath = cmdElement.Attribute("targetPath").ValueNullSafe();
                var isCritical = cmdElement.Attribute("isCritical").ValueOr<bool>(defaultCriticalValue);
                var priority = cmdElement.Attribute("priority").ValueOr<int>(3);

                //Any validation here is not a good idea. Commands should be left over in an invalid state, 
                //so eventually installation will fail if they are marked as critical.
                var newCmd = new CopyModFileCmd();
                //We set up path type to a expected path type.
                newCmd.SourcePath = new InstallEntityPath(sourcePath);
                newCmd.TargetPath = new InstallEntityPath(targetPath);
                newCmd.IsCritical = isCritical;
                newCmd.Priority = priority;

                result.Add(newCmd);
            }

            return result;
        }

        private IEnumerable<CopyGameFileCmd> ReadCopyGameFileCmds(XElement source)
        {
            var result = new List<CopyGameFileCmd>();

            var cmdElementsCollection = source.Elements(CmdEntryType.CopyGameFile.Name);
            foreach (var cmdElement in cmdElementsCollection)
            {
                var sourcePath = cmdElement.Attribute("sourcePath").ValueNullSafe();
                var targetPath = cmdElement.Attribute("targetPath").ValueNullSafe();
                var isCritical = cmdElement.Attribute("isCritical").ValueOr<bool>(defaultCriticalValue);
                var priority = cmdElement.Attribute("priority").ValueOr<int>(4);

                var newCmd = new CopyGameFileCmd();
                newCmd.SourcePath = new InstallEntityPath(sourcePath);
                newCmd.TargetPath = new InstallEntityPath(targetPath);
                newCmd.IsCritical = isCritical;
                newCmd.Priority = priority;

                result.Add(newCmd);
            }

            return result;
        }

        private IEnumerable<RemoveFileCmd> ReadRemoveFileCmds(XElement source)
        {
            var result = new List<RemoveFileCmd>();

            var cmdElementsCollection = source.Elements(CmdEntryType.RemoveFile.Name);
            foreach (var cmdElement in cmdElementsCollection)
            {
                var sourcePath = cmdElement.Attribute("sourcePath").ValueNullSafe();
                var isCritical = cmdElement.Attribute("isCritical").ValueOr<bool>(defaultCriticalValue);
                var priority = cmdElement.Attribute("priority").ValueOr<int>(1);

                var newCmd = new RemoveFileCmd();
                newCmd.SourcePath = new InstallEntityPath(sourcePath);
                newCmd.IsCritical = isCritical;
                newCmd.Priority = priority;

                result.Add(newCmd);
            }

            return result;
        }

        private IEnumerable<ReplaceImageCmd> ReadReplaceImageCmds(XElement source)
        {
            var result = new List<ReplaceImageCmd>();

            var cmdElementsCollection = source.Elements(CmdEntryType.ReplaceImage.Name);
            foreach (var cmdElement in cmdElementsCollection)
            {
                var sourcePath = cmdElement.Attribute("sourcePath").ValueNullSafe();
                var targetPath = cmdElement.Attribute("targetPath").ValueNullSafe();
                var edataImagePath = cmdElement.Attribute("targetContentPath").ValueNullSafe();
                var isCritical = cmdElement.Attribute("isCritical").ValueOr<bool>(defaultCriticalValue);
                var priority = cmdElement.Attribute("priority").ValueOr<int>(2);

                var newCmd = new ReplaceImageCmd();
                newCmd.SourcePath = new InstallEntityPath(sourcePath);
                newCmd.TargetPath = new InstallEntityPath(targetPath);
                newCmd.TargetContentPath = new ContentPath(edataImagePath);
                newCmd.IsCritical = isCritical;
                newCmd.Priority = priority;

                result.Add(newCmd);
            }

            return result;
        }

        private IEnumerable<ReplaceImageTileCmd> ReadReplaceImageTileCmds(XElement source)
        {
            var result = new List<ReplaceImageTileCmd>();

            var cmdElementsCollection = source.Elements(CmdEntryType.ReplaceImageTile.Name);
            foreach (var cmdElement in cmdElementsCollection)
            {
                var sourcePath = cmdElement.Attribute("sourcePath").ValueNullSafe();
                var targetPath = cmdElement.Attribute("targetPath").ValueNullSafe();
                var edataImagePath = cmdElement.Attribute("targetContentPath").ValueNullSafe();
                var column = cmdElement.Attribute("column").ValueOrDefault<int?>();
                var row = cmdElement.Attribute("row").ValueOrDefault<int?>();
                var tileSize = cmdElement.Attribute("tileSize").ValueOr<int>(256);
                var isCritical = cmdElement.Attribute("isCritical").ValueOr<bool>(defaultCriticalValue);
                var priority = cmdElement.Attribute("priority").ValueOr<int>(2);

                var newCmd = new ReplaceImageTileCmd();
                newCmd.SourcePath = new InstallEntityPath(sourcePath);
                newCmd.TargetPath = new InstallEntityPath(targetPath);
                newCmd.TargetContentPath = new ContentPath(edataImagePath);
                newCmd.Column = column;
                newCmd.Row = row;
                newCmd.TileSize = tileSize;
                newCmd.IsCritical = isCritical;
                newCmd.Priority = priority;

                result.Add(newCmd);
            }

            return result;
        }

        private IEnumerable<ReplaceImagePartCmd> ReadReplaceImagePartCmds(XElement source)
        {
            var result = new List<ReplaceImagePartCmd>();

            var cmdElementsCollection = source.Elements(CmdEntryType.ReplaceImagePart.Name); 
            foreach (var cmdElement in cmdElementsCollection)
            {
                var sourcePath = cmdElement.Attribute("sourcePath").ValueNullSafe();
                var targetPath = cmdElement.Attribute("targetPath").ValueNullSafe();
                var edataImagePath = cmdElement.Attribute("targetContentPath").ValueNullSafe();
                var xPos = cmdElement.Attribute("xPos").ValueOr<int>(0);
                var yPos = cmdElement.Attribute("yPos").ValueOr<int>(0);
                var isCritical = cmdElement.Attribute("isCritical").ValueOr<bool>(defaultCriticalValue);
                var priority = cmdElement.Attribute("priority").ValueOr<int>(2);

                var newCmd = new ReplaceImagePartCmd();
                newCmd.SourcePath = new InstallEntityPath(sourcePath);
                newCmd.TargetPath = new InstallEntityPath(targetPath);
                newCmd.TargetContentPath = new ContentPath(edataImagePath);
                newCmd.XPosition = xPos;
                newCmd.YPosition = yPos;
                newCmd.IsCritical = isCritical;
                newCmd.Priority = priority;

                result.Add(newCmd);
            }

            return result;
        }

        private IEnumerable<ReplaceContentCmd> ReadReplaceContentCmds(XElement source)
        {
            var result = new List<ReplaceContentCmd>();

            var cmdElementsCollection = source.Elements(CmdEntryType.ReplaceContent.Name);
            foreach (var cmdElement in cmdElementsCollection)
            {
                var sourcePath = cmdElement.Attribute("sourcePath").ValueNullSafe();
                var targetPath = cmdElement.Attribute("targetPath").ValueNullSafe();
                var edataContentPath = cmdElement.Attribute("targetContentPath").ValueNullSafe();
                var isCritical = cmdElement.Attribute("isCritical").ValueOr<bool>(defaultCriticalValue);
                var priority = cmdElement.Attribute("priority").ValueOr<int>(2);

                var newCmd = new ReplaceContentCmd();
                newCmd.SourcePath = new InstallEntityPath(sourcePath);
                newCmd.TargetPath = new InstallEntityPath(targetPath);
                newCmd.TargetContentPath = new ContentPath(edataContentPath);
                newCmd.IsCritical = isCritical;
                newCmd.Priority = priority;

                result.Add(newCmd);
            }

            return result;
        }

        private IEnumerable<AlterDictionaryCmd> ReadAlterDictionaryCmds(XElement source)
        {
            var result = new List<AlterDictionaryCmd>();

            var cmdElementsCollection = source.Elements(CmdEntryType.AlterDictionary.Name);
            foreach (var cmdElement in cmdElementsCollection)
            {
                var targetPath = cmdElement.Attribute("targetPath").ValueNullSafe();
                var dictionaryPath = cmdElement.Attribute("targetContentPath").ValueNullSafe();
                var isCritical = cmdElement.Attribute("isCritical").ValueOr<bool>(defaultCriticalValue);
                var priority = cmdElement.Attribute("priority").ValueOr<int>(2);

                //Read Entries
                var entries = new List<KeyValuePair<String, String>>();
                var entriesElements = cmdElement.Elements("Entry");
                foreach (var entryElement in entriesElements)
                {
                    //First try to read the attribute, because an empty tag element value returns an empty string not null.
                    var value = entryElement.Attribute("value").ValueNullSafe() ??
                        entryElement.ValueNullSafe();

                    var hash = entryElement.Attribute("hash").ValueNullSafe();
                    if (hash != null)
                    {
                        var newEntry = new KeyValuePair<String, String>(hash, value);
                        entries.Add(newEntry);
                    }
                    else
                    {
                        var line = (entryElement as IXmlLineInfo).LineNumber;
                        var warning = String.Format("Entry at line: \"{0}\" was ignored. It doesn't contain a specified hash attribute.", line);
                        Common.Logging.LoggerFactory.Create(this.GetType()).Warn(warning);
                    }
                }

                var newCmd = new AlterDictionaryCmd();
                newCmd.TargetPath = new InstallEntityPath(targetPath);
                newCmd.TargetContentPath = new ContentPath(dictionaryPath);
                newCmd.AlteredEntries = entries;
                newCmd.IsCritical = isCritical;
                newCmd.Priority = priority;

                result.Add(newCmd);
            }

            return result;
        }

        #region Nested Class GroupProductionRule

        protected class GroupProductionRule
        {
            public GroupProductionRule(int priority,
                Func<IEnumerable<IInstallCmd>, IEnumerable<ICmdGroup>> productionRule)
            {
                this.Priority = priority;
                this.ProduceGroup = productionRule;
            }

            public int Priority { get; private set; }
            public Func<IEnumerable<IInstallCmd>, IEnumerable<ICmdGroup>> ProduceGroup { get; private set; }
        }

        #endregion //Nested Class GroupProductionRule

    }

}
