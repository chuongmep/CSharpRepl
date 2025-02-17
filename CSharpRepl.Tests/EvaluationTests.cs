// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using CSharpRepl.Services;
using CSharpRepl.Services.Roslyn;
using CSharpRepl.Services.Roslyn.Scripting;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace CSharpRepl.Tests;

[Collection(nameof(RoslynServices))]
public class EvaluationTests : IAsyncLifetime
{
    private readonly RoslynServices services;
    private readonly StringBuilder stdout;

    public EvaluationTests()
    {
        var (console, stdout) = FakeConsole.CreateStubbedOutput();
        this.services = new RoslynServices(console, new Configuration(), new TestTraceLogger());
        this.stdout = stdout;
    }

    public Task InitializeAsync() => services.WarmUpAsync(Array.Empty<string>());
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Evaluate_LiteralInteger_ReturnsInteger()
    {
        var result = await services.EvaluateAsync("5");

        var success = Assert.IsType<EvaluationResult.Success>(result);
        Assert.Equal("5", success.Input);

        var returnValue = Assert.IsType<int>(success.ReturnValue);
        Assert.Equal(5, returnValue);
    }

    [Fact]
    public async Task Evaluate_Variable_ReturnsValue()
    {
        var variableAssignment = await services.EvaluateAsync(@"var x = ""Hello World"";");
        var variableUsage = await services.EvaluateAsync(@"x.Replace(""World"", ""Mundo"")");

        var assignment = Assert.IsType<EvaluationResult.Success>(variableAssignment);
        var usage = Assert.IsType<EvaluationResult.Success>(variableUsage);
        Assert.Null(assignment.ReturnValue);
        Assert.Equal("Hello Mundo", usage.ReturnValue);
    }

    [Fact]
    public async Task Evaluate_NugetPackage_InstallsPackage()
    {
        var installation = await services.EvaluateAsync(@"#r ""nuget:Newtonsoft.Json""");
        var usage = await services.EvaluateAsync(@"Newtonsoft.Json.JsonConvert.SerializeObject(new { Foo = ""bar"" })");

        var installationResult = Assert.IsType<EvaluationResult.Success>(installation);
        var usageResult = Assert.IsType<EvaluationResult.Success>(usage);

        Assert.Null(installationResult.ReturnValue);
        Assert.Contains(installationResult.References, r => r.Display.EndsWith("Newtonsoft.Json.dll"));
        Assert.Contains("Adding references for Newtonsoft.Json", stdout.ToString());
        Assert.Equal(@"{""Foo"":""bar""}", usageResult.ReturnValue);
    }

    [Fact]
    public async Task Evaluate_NugetPackageVersioned_InstallsPackageVersion()
    {
        var installation = await services.EvaluateAsync(@"#r ""nuget:Microsoft.CodeAnalysis.CSharp, 3.11.0""");
        var usage = await services.EvaluateAsync(@"Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(""5"")");

        var installationResult = Assert.IsType<EvaluationResult.Success>(installation);
        var usageResult = Assert.IsType<EvaluationResult.Success>(usage);

        Assert.Null(installationResult.ReturnValue);
        Assert.NotNull(usageResult.ReturnValue);
        Assert.Contains("Adding references for Microsoft.CodeAnalysis.CSharp.3.11.0", stdout.ToString());
    }

    [Fact]
    public async Task Evaluate_RelativeAssemblyReference_CanReferenceAssembly()
    {
        var referenceResult = await services.EvaluateAsync(@"#r ""./Data/DemoLibrary.dll""");
        var importResult = await services.EvaluateAsync("using DemoLibrary;");
        var multiplyResult = await services.EvaluateAsync("DemoClass.Multiply(5, 6)");

        Assert.IsType<EvaluationResult.Success>(referenceResult);
        Assert.IsType<EvaluationResult.Success>(importResult);
        var successfulResult = Assert.IsType<EvaluationResult.Success>(multiplyResult);
        Assert.Equal(30, successfulResult.ReturnValue);
    }

    [Fact]
    public async Task Evaluate_AbsoluteAssemblyReference_CanReferenceAssembly()
    {
        var absolutePath = Path.GetFullPath("./Data/DemoLibrary.dll");
        var referenceResult = await services.EvaluateAsync(@$"#r ""{absolutePath}""");
        var importResult = await services.EvaluateAsync("using DemoLibrary;");
        var multiplyResult = await services.EvaluateAsync("DemoClass.Multiply(7, 6)");

        Assert.IsType<EvaluationResult.Success>(referenceResult);
        Assert.IsType<EvaluationResult.Success>(importResult);
        var successfulResult = Assert.IsType<EvaluationResult.Success>(multiplyResult);
        Assert.Equal(42, successfulResult.ReturnValue);
    }

    [Fact]
    public async Task Evaluate_AssemblyReferenceInSearchPath_CanReferenceAssembly()
    {
        var referenceResult = await services.EvaluateAsync(@"#r ""System.Linq.dll""");

        Assert.IsType<EvaluationResult.Success>(referenceResult);
    }

    [Fact]
    public async Task Evaluate_AssemblyReferenceWithSharedFramework_ReferencesSharedFramework()
    {
        var referenceResult = await services.EvaluateAsync(@"#r ""./Data/WebApplication1.dll""");
        var sharedFrameworkResult = await services.EvaluateAsync(@"using Microsoft.AspNetCore.Hosting;");
        var applicationResult = await services.EvaluateAsync(@"using WebApplication1;");

        Assert.IsType<EvaluationResult.Success>(referenceResult);
        Assert.IsType<EvaluationResult.Success>(sharedFrameworkResult);
        Assert.IsType<EvaluationResult.Success>(applicationResult);

        var completions = await services.CompleteAsync(@"using WebApplicat", 17);
        Assert.Contains("WebApplication1", completions.Select(c => c.Item.DisplayText).First(text => text.StartsWith("WebApplicat")));
    }

    [Fact]
    public async Task Evaluate_ProjectReference_ReferencesProject()
    {
        var referenceResult = await services.EvaluateAsync(@"#r ""./../../../../CSharpRepl.Services/CSharpRepl.Services.csproj""");
        var importResult = await services.EvaluateAsync(@"using CSharpRepl.Services;");

        Assert.IsType<EvaluationResult.Success>(referenceResult);
        Assert.IsType<EvaluationResult.Success>(importResult);
    }
}
