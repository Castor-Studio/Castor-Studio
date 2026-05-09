using CastorApplication.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CastorApplication.ViewModels.Settings.Sections
{
    public abstract partial class SettingsSectionViewModel : ViewModelBase, ISettingsSection
    {
        private bool _isLoading;
        
        [ObservableProperty]
        private bool _isDirty;

        protected SettingsSectionViewModel()
        {
            PropertyChanged += (_, e) =>
            {
                if (_isLoading || e.PropertyName is null || e.PropertyName == nameof(IsDirty))
                {
                    return;
                }

                IsDirty = true;
            };
        }

        public void Load(ApplicationSettings settings)
        {
            _isLoading = true;
            try
            {
                LoadCore(settings);
                IsDirty = false;
            }
            finally
            {
                _isLoading = false;
            }
        }

        public void Save(ApplicationSettings settings)
        {
            SaveCore(settings);
        }

        public void MarkClean()
        {
            IsDirty = false;
        }

        protected abstract void LoadCore(ApplicationSettings settings);
        protected abstract void SaveCore(ApplicationSettings settings);
    }
}
