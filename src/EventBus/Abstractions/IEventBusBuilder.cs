using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EventBus.Abstractions;

public interface IEventBusBuilder
{
    public IServiceCollection Services { get; }
}