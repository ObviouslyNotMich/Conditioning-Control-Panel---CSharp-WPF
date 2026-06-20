using System.Threading.Tasks;

namespace ConditioningControlPanel.Core.Services.Commands
{
    public interface ICommand
    {
        Task<bool> ExecuteAsync();
    }
}
