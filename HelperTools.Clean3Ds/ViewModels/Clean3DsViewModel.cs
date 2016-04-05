﻿namespace HelperTools.Clean3Ds.ViewModels
{
    using System;
    using System.Collections.ObjectModel;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using System.Windows;
    using Infrastructure.Base;
    using Infrastructure.Constants;
    using Infrastructure.Events;
    using Infrastructure.Interfaces;
    using Infrastructure.Properties;
    using Infrastructure.Services;
    using MahApps.Metro.Controls;
    using MahApps.Metro.Controls.Dialogs;
    using Models;
    using Prism.Commands;

    /// <summary>The Clean3DsViewModel.</summary>
    /// <seealso cref="ViewModelBase" />
    public class Clean3DsViewModel : ViewModelBase
    {
        #region Fields
        private const string EXTENSION = "*.3ds";

        private string selectedPath;

        private ObservableCollection<IFiles> fileCollection = new ObservableCollection<IFiles>();
        private ObservableCollection<IFiles> selectedFilesCollection = new ObservableCollection<IFiles>();
        #endregion Fields

        #region Constructor
        /// <summary>Initializes a new instance of the <see cref="Clean3DsViewModel" /> class.</summary>
        public Clean3DsViewModel()
        {
            EventAggregator.GetEvent<SelectedPathUpdateEvent>().Subscribe(OnSelectedPathUpdateEvent);
            EventAggregator.GetEvent<SelectedFilesUpdateEvent>().Subscribe(OnSelectedFilesUpdateEvent);

            SelectedPath = PathNames.DesktopPath;

            GetFiles();

            StartGenerationCommand = new DelegateCommand(StartGeneration, CanStartGeneration);
        }
        #endregion Constructor

        #region Properties
        /// <summary>Gets and sets the path, where the Files are.</summary>
        /// <value>The selected path.</value>
        public string SelectedPath
        {
            get { return selectedPath; }
            set { SetProperty(ref selectedPath, value); }
        }

        /// <summary>Gets and sets a collection of the files in the selected path.</summary>
        /// <value>The file collection.</value>
        public ObservableCollection<IFiles> FileCollection
        {
            get { return fileCollection; }
            set { SetProperty(ref fileCollection, value); }
        }

        /// <summary>Gets and sets a collection of all selected files.</summary>
        /// <value>The selected files collection.</value>
        public ObservableCollection<IFiles> SelectedFilesCollection
        {
            get { return selectedFilesCollection; }
            set { SetProperty(ref selectedFilesCollection, value); }
        }

        /// <summary>Gets the command to start the working thread.</summary>
        /// <value>The start generation command.</value>
        public DelegateCommand StartGenerationCommand { get; }
        #endregion Properties

        #region Event-Handler
        private void OnSelectedPathUpdateEvent(string path)
        {
            SelectedPath = path;

            GetFiles();
        }

        private void OnSelectedFilesUpdateEvent(ObservableCollection<IFiles> files)
        {
            SelectedFilesCollection = files;

            StartGenerationCommand.RaiseCanExecuteChanged();
        }
        #endregion Event-Handler

        #region Methods
        private void GetFiles()
        {
            FileCollection.Clear();

            foreach (var file in Directory.GetFiles(SelectedPath, EXTENSION))
            {
                FileCollection.Add(new Clean3DsFiles()
                {
                    FullFilePath = Path.GetFullPath(file),
                    FileName = Path.GetFileNameWithoutExtension(file),
                    CreatedTime = File.GetCreationTimeUtc(file).ToLocalTime(),
                    IsSelected = false
                });
            }

            EventAggregator.GetEvent<FilesUpdateEvent>().Publish(FileCollection);

            if (FileCollection.Count < 1) return;

            StartGenerationCommand.RaiseCanExecuteChanged();
        }

        private bool CanStartGeneration()
        {
            return SelectedFilesCollection.Count > 0;
        }

        private async void StartGeneration()
        {
            var window = Application.Current.Windows.OfType<MetroWindow>().FirstOrDefault();

            var controller = await window.ShowProgressAsync(Resources.ProgressDialogTitle, Resources.ProgressDialogPreviewContent);

            controller.SetIndeterminate();
            controller.SetCancelable(true);

            int sumFiles = SelectedFilesCollection.Count;
            int curFile = 0;

            controller.Maximum = sumFiles;

            await Task.Delay(250);

            if (!Directory.Exists(PathNames.TempFolderPath))
            {
                Directory.CreateDirectory(PathNames.TempFolderPath);
            }

            foreach (var file in SelectedFilesCollection.Where(file => File.Exists(file.FullFilePath)))
            {
                try
                {
                    var tmpFile = PathNames.TempFolderPath + CharConverterService.ConvertCharsToAscii(file.FileName + EXTENSION);

                    File.Copy(file.FullFilePath, tmpFile, true);

                    await Task.Delay(50);

                    ExternalProgramService.OpenThirdParty("fix3ds.exe", " -m \"" + tmpFile + "\"", PathNames.ThirdPartyPath);

                    await Task.Delay(50);

                    File.Delete(file.FullFilePath);
                    File.Copy(tmpFile, SelectedPath + Path.GetFileName(tmpFile), true);
                    File.SetCreationTime(SelectedPath + Path.GetFileName(tmpFile), DateTime.Now);

                    await Task.Delay(50);

                    controller.SetProgress(++curFile);
                    controller.SetMessage(string.Format(Resources.ProgressDialogRunningContent, curFile, sumFiles));

                    await Task.Delay(50);
                }
                catch (Exception ex)
                {
                    DialogService.Exception(ex, DialogService.ExceptionType.Universal);
                }
            }

            if (Directory.Exists(PathNames.TempFolderPath))
            {
                Directory.Delete(PathNames.TempFolderPath, true);
            }

            await controller.CloseAsync();

            GetFiles();

            if (controller.IsCanceled)
            {
                await window.ShowMessageAsync(Resources.MessageDialogCancelTitle, Resources.MessageDialogCancelContent);

                return;
            }

            if (
                await
                    window.ShowMessageAsync(Resources.MessageDialogCompleteTitle, string.Format(Resources.MessageDialogCompleteContent, "\n"),
                        MessageDialogStyle.AffirmativeAndNegative) == MessageDialogResult.Affirmative)
            {
                ExternalProgramService.OpenExplorer(SelectedPath);
            }
        }
        #endregion Methods
    }
}