using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Sagaway.ReservationDemo.ReservationUI;
using Sagaway.ReservationDemo.ReservationUI.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(_ => new HttpClient());
builder.Services.AddScoped<IReservationApiClient, ReservationApiClient>();
builder.Services.AddSingleton<ISignalRService, SignalRService>();

await builder.Build().RunAsync();
