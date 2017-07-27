﻿#if !NET40
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using WampSharp.Core.Serialization;
using WampSharp.Tests.Wampv2.TestHelpers.Integration;
using WampSharp.V2;
using WampSharp.V2.Core;
using WampSharp.V2.Core.Contracts;
using WampSharp.V2.Rpc;

namespace WampSharp.Tests.Wampv2.Integration
{
    public class RpcProgressTests
    {
        [Test]
        public async Task ProgressiveCallsCallerProgress()
        {
            WampPlayground playground = new WampPlayground();

            CallerCallee dualChannel = await playground.GetCallerCalleeDualChannel();
            IWampChannel calleeChannel = dualChannel.CalleeChannel;
            IWampChannel callerChannel = dualChannel.CallerChannel;

            await calleeChannel.RealmProxy.Services.RegisterCallee(new LongOpService());

            MyCallback callback = new MyCallback();

            callerChannel.RealmProxy.RpcCatalog.Invoke
                (callback,
                    new CallOptions() {ReceiveProgress = true},
                    "com.myapp.longop",
                    new object[] {10});

            callback.Task.Wait(2000);

            CollectionAssert.AreEquivalent(Enumerable.Range(0, 10), callback.ProgressiveResults);
            Assert.That(callback.Task.Result, Is.EqualTo(10));
        }


        [Test]
        public async Task CancelProgressiveCallsCalleeCancellationToken()
        {
            WampPlayground playground = new WampPlayground();

            CallerCallee dualChannel = await playground.GetCallerCalleeDualChannel();
            IWampChannel calleeChannel = dualChannel.CalleeChannel;
            IWampChannel callerChannel = dualChannel.CallerChannel;

            CancelableLongOpService service = new CancelableLongOpService();
            await calleeChannel.RealmProxy.Services.RegisterCallee(service);

            MyCallback callback = new MyCallback();

            IWampCancellableInvocationProxy cancellable =
                callerChannel.RealmProxy.RpcCatalog.Invoke
                (callback,
                 new CallOptions() {ReceiveProgress = true},
                 "com.myapp.longop",
                 new object[] {100});

            Assert.That(service.CancellationToken, Is.Not.Null);
            Assert.That(service.CancellationToken.IsCancellationRequested, Is.False);

            cancellable.Cancel(new CancelOptions());

            Assert.That(service.CancellationToken.IsCancellationRequested, Is.True);
        }

        [Test]
        public async Task ProgressiveCallsCalleeProxyProgress()
        {
            WampPlayground playground = new WampPlayground();

            CallerCallee dualChannel = await playground.GetCallerCalleeDualChannel();
            IWampChannel calleeChannel = dualChannel.CalleeChannel;
            IWampChannel callerChannel = dualChannel.CallerChannel;

            MyOperation myOperation = new MyOperation();

            await calleeChannel.RealmProxy.RpcCatalog.Register(myOperation, new RegisterOptions());
            ILongOpService proxy = callerChannel.RealmProxy.Services.GetCalleeProxyPortable<ILongOpService>();

            List<int> results = new List<int>();
            MyProgress<int> progress = new MyProgress<int>(i => results.Add(i));

            Task<int> result = proxy.LongOp(10, progress);
            result.Wait();

            CollectionAssert.AreEquivalent(Enumerable.Range(0, 10), results);

            Assert.That(result.Result, Is.EqualTo(10));
        }

        [Test]
        public async Task ProgressiveCancellationTokenCancelCallsInterrupt()
        {
            WampPlayground playground = new WampPlayground();

            CallerCallee dualChannel = await playground.GetCallerCalleeDualChannel();
            IWampChannel calleeChannel = dualChannel.CalleeChannel;
            IWampChannel callerChannel = dualChannel.CallerChannel;

            MyCancellableOperation myOperation = new MyCancellableOperation();

            await calleeChannel.RealmProxy.RpcCatalog.Register(myOperation, new RegisterOptions());
            ICancellableLongOpService proxy = callerChannel.RealmProxy.Services.GetCalleeProxyPortable<ICancellableLongOpService>();

            CancellationTokenSource tokenSource = new CancellationTokenSource();
            MyProgress<int> progress = new MyProgress<int>(x => { });

            Task<int> result = proxy.LongOp(10, progress, tokenSource.Token);
            Assert.That(myOperation.CancellableInvocation.InterruptCalled, Is.False);

            tokenSource.Cancel();

            Assert.That(myOperation.CancellableInvocation.InterruptCalled, Is.True);
        }

        public class MyCancellableOperation : IWampRpcOperation
        {
            public MyCancellableInvocation CancellableInvocation { get; private set; }

            public string Procedure
            {
                get
                {
                    return "com.myapp.longop";
                }
            }

            public IWampCancellableInvocation Invoke<TMessage>(IWampRawRpcOperationRouterCallback caller, IWampFormatter<TMessage> formatter, InvocationDetails details)
            {
                return null;
            }

            public IWampCancellableInvocation Invoke<TMessage>(IWampRawRpcOperationRouterCallback caller, IWampFormatter<TMessage> formatter,
                                                               InvocationDetails details,
                                                               TMessage[] arguments)
            {
                CancellableInvocation = new MyCancellableInvocation();
                return CancellableInvocation;
            }

            public IWampCancellableInvocation Invoke<TMessage>(IWampRawRpcOperationRouterCallback caller, IWampFormatter<TMessage> formatter, InvocationDetails details,
                                                               TMessage[] arguments, IDictionary<string, TMessage> argumentsKeywords)
            {
                return null;
            }
        }

        public class MyOperation : IWampRpcOperation
        {
            public string Procedure
            {
                get
                {
                    return "com.myapp.longop";
                }
            }

            public IWampCancellableInvocation Invoke<TMessage>(IWampRawRpcOperationRouterCallback caller, IWampFormatter<TMessage> formatter, InvocationDetails details)
            {
                return null;
            }

            public IWampCancellableInvocation Invoke<TMessage>(IWampRawRpcOperationRouterCallback caller, IWampFormatter<TMessage> formatter,
                InvocationDetails details,
                TMessage[] arguments)
            {
                TMessage number = arguments[0];
                int n = formatter.Deserialize<int>(number);

                for (int i = 0; i < n; i++)
                {
                    caller.Result(WampObjectFormatter.Value,
                        new YieldOptions {Progress = true},
                        new object[] {i});
                }

                caller.Result(WampObjectFormatter.Value,
                    new YieldOptions(),
                    new object[] {n});

                return null;
            }

            public IWampCancellableInvocation Invoke<TMessage>(IWampRawRpcOperationRouterCallback caller, IWampFormatter<TMessage> formatter, InvocationDetails details,
                TMessage[] arguments, IDictionary<string, TMessage> argumentsKeywords)
            {
                return null;
            }
        }

        public interface ILongOpService
        {
            [WampProcedure("com.myapp.longop")]
            [WampProgressiveResultProcedure]
            Task<int> LongOp(int n, IProgress<int> progress);
        }

        public interface ICancellableLongOpService
        {
            [WampProcedure("com.myapp.longop")]
            [WampProgressiveResultProcedure]
            Task<int> LongOp(int n, IProgress<int> progress, CancellationToken cancellationToken);
        }


        public class LongOpService : ILongOpService
        {
            public async Task<int> LongOp(int n, IProgress<int> progress)
            {
                for (int i = 0; i < n; i++)
                {
                    progress.Report(i);
                    await Task.Delay(100);
                }

                return n;
            }
        }

        public class MyCallback : IWampRawRpcOperationClientCallback
        {
            private readonly List<int> mProgressiveResults = new List<int>();
            private readonly TaskCompletionSource<int> mTask = new TaskCompletionSource<int>();

            public List<int> ProgressiveResults
            {
                get
                {
                    return mProgressiveResults;
                }
            }

            public Task<int> Task
            {
                get { return mTask.Task; }
            }

            public void Result<TMessage>(IWampFormatter<TMessage> formatter, ResultDetails details)
            {
                throw new NotImplementedException();
            }

            public void Result<TMessage>(IWampFormatter<TMessage> formatter, ResultDetails details, TMessage[] arguments)
            {
                int current = formatter.Deserialize<int>(arguments[0]);

                if (details.Progress == true)
                {
                    mProgressiveResults.Add(current);
                }
                else
                {
                    mTask.SetResult(current);
                }
            }

            public void Result<TMessage>(IWampFormatter<TMessage> formatter, ResultDetails details, TMessage[] arguments,
                IDictionary<string, TMessage> argumentsKeywords)
            {
                throw new NotImplementedException();
            }

            public void Error<TMessage>(IWampFormatter<TMessage> formatter, TMessage details, string error)
            {
                throw new NotImplementedException();
            }

            public void Error<TMessage>(IWampFormatter<TMessage> formatter, TMessage details, string error, TMessage[] arguments)
            {
                throw new NotImplementedException();
            }

            public void Error<TMessage>(IWampFormatter<TMessage> formatter, TMessage details, string error, TMessage[] arguments,
                TMessage argumentsKeywords)
            {
                throw new NotImplementedException();
            }
        }
    }

    public class MyCancellableInvocation : IWampCancellableInvocation
    {
        public void Cancel(InterruptOptions options)
        {
            InterruptCalled = true;
        }

        public bool InterruptCalled { get; set; }
    }

    public class CancelableLongOpService
    {
        public CancellationToken CancellationToken { get; private set; }

        [WampProcedure("com.myapp.longop")]
        [WampProgressiveResultProcedure]
        public async Task<int> LongOp(int n, IProgress<int> progress, CancellationToken cancellationToken)
        {
            CancellationToken = cancellationToken;
            for (int i = 0; i < n; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw new WampException(WampErrors.Canceled);
                }

                progress.Report(i);
                await Task.Delay(100);
            }

            return n;
        }
    }

    internal class MyProgress<T> : IProgress<T>
    {
        private readonly Action<T> mAction;

        public MyProgress(Action<T> action)
        {
            mAction = action;
        }

        public void Report(T value)
        {
            mAction(value);
        }
    }
}
#endif