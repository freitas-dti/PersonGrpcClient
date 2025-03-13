using Microsoft.Extensions.Logging;
using PersonGrpcClient.Services;
using PersonGrpcClient.ViewModels;

namespace PersonGrpcClient;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

        // Registrar serviços
        builder.Services.AddSingleton<DatabaseService>();
        builder.Services.AddSingleton<GrpcClientService>();

        // Registrar IConnectivity
        builder.Services.AddSingleton<IConnectivity>(Connectivity.Current);

        // Registrar IDispatcher
        builder.Services.AddSingleton<IDispatcher>(Dispatcher.GetForCurrentThread());

        // Registrar ViewModel e Page
        builder.Services.AddSingleton<MainPageViewModel>();
        builder.Services.AddSingleton<MainPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
