using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PersonGrpcClient.Services
{
    class ConnectivityService
    {
        private readonly IConnectivity _connectivity;
        private readonly IDispatcher _dispatcher;

        public event EventHandler<bool> ConnectivityChanged;

        public ConnectivityService(IConnectivity connectivity, IDispatcher dispatcher)
        {
            _connectivity = connectivity;
            _dispatcher = dispatcher;
            _connectivity.ConnectivityChanged += OnConnectivityChanged;
        }

        public bool IsConnected => _connectivity.NetworkAccess == NetworkAccess.Internet;

        private void OnConnectivityChanged(object sender, ConnectivityChangedEventArgs e)
        {
            _dispatcher.Dispatch(() =>
            {
                ConnectivityChanged?.Invoke(this, IsConnected);
            });
        }
    }
}
