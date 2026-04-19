using dvmig.Providers;
using System;
using System.Threading.Tasks;

namespace dvmig.App.Services
{
    public interface IMigrationService
    {
        Task<bool> TestConnectionAsync(string connectionString, bool isLegacy);
        IDataverseProvider? SourceProvider { get; }
        IDataverseProvider? TargetProvider { get; }
    }

    public class MigrationService : IMigrationService
    {
        public IDataverseProvider? SourceProvider { get; private set; }
        public IDataverseProvider? TargetProvider { get; private set; }

        public async Task<bool> TestConnectionAsync(string connectionString, bool isLegacy)
        {
            try
            {
                // In a real app, we would dispose the old one
                if (isLegacy)
                {
                    await Task.Run(() => new LegacyCrmProvider(connectionString));
                }
                else
                {
                    await Task.Run(() => new DataverseProvider(connectionString));
                }
                
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
