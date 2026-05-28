using System.Collections.Generic;
using TID3.ViewModels;
using Xunit;

namespace TID3.Tests
{
    public class ViewModelInfrastructureTests
    {
        // A minimal concrete view model exercising SetProperty.
        private sealed class SampleViewModel : ViewModelBase
        {
            private string _name = "";
            public string Name
            {
                get => _name;
                set => SetProperty(ref _name, value);
            }
        }

        // ---- ViewModelBase.SetProperty -----------------------------------------

        [Fact]
        public void SetProperty_RaisesPropertyChanged_WhenValueChanges()
        {
            var vm = new SampleViewModel();
            var raised = new List<string?>();
            vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            vm.Name = "Radiohead";

            Assert.Equal(new[] { "Name" }, raised);
            Assert.Equal("Radiohead", vm.Name);
        }

        [Fact]
        public void SetProperty_DoesNotRaise_WhenValueUnchanged()
        {
            var vm = new SampleViewModel { Name = "Same" };
            var raised = 0;
            vm.PropertyChanged += (_, _) => raised++;

            vm.Name = "Same"; // no change

            Assert.Equal(0, raised);
        }

        // ---- RelayCommand -------------------------------------------------------

        [Fact]
        public void RelayCommand_Execute_InvokesAction()
        {
            var ran = false;
            var cmd = new RelayCommand(() => ran = true);

            cmd.Execute(null);

            Assert.True(ran);
        }

        [Fact]
        public void RelayCommand_CanExecute_DefaultsToTrue()
        {
            var cmd = new RelayCommand(() => { });

            Assert.True(cmd.CanExecute(null));
        }

        [Fact]
        public void RelayCommand_CanExecute_RespectsPredicate()
        {
            var allow = false;
            var cmd = new RelayCommand(() => { }, () => allow);

            Assert.False(cmd.CanExecute(null));
            allow = true;
            Assert.True(cmd.CanExecute(null));
        }

        [Fact]
        public void RelayCommand_PassesParameterToAction()
        {
            object? received = null;
            var cmd = new RelayCommand(p => received = p);

            cmd.Execute("payload");

            Assert.Equal("payload", received);
        }

        [Fact]
        public void AsyncRelayCommand_CanExecute_FalseWhilePredicateFalse()
        {
            var cmd = new AsyncRelayCommand(() => System.Threading.Tasks.Task.CompletedTask, () => false);

            Assert.False(cmd.CanExecute(null));
        }
    }
}
