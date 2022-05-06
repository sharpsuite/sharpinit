using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;

using NLog;

using SharpInit.Units;

namespace SharpInit.Tasks
{
    public enum TaskExecutionState
    {
        Unknown,
        Registered,
        Enqueued,
        Executing,
        Finished,
        Aborted
    }

    public class TaskExecution
    {
        public ServiceManager ServiceManager { get; set; }
        public UnitRegistry Registry => ServiceManager.Registry;
        public TaskRunner Runner => ServiceManager.Runner;

        public Task Task { get; set; }
        public TaskResult Result { get; set; }
        public TaskExecutionState State { get; set; }
        public TaskContext Context { get; set; }
        public System.Threading.Tasks.Task NativeTask { get; set; }
        public Stack<long> ParentTasks { get; set; }

        internal ManualResetEventSlim done = new ManualResetEventSlim(false);
        internal CancellationTokenSource done_cts = new CancellationTokenSource();

        internal TaskExecution(TaskRunner runner, Task task, TaskContext context)
        {
            ServiceManager = runner.ServiceManager;
            Task = task;
            Context = context;
            State = TaskExecutionState.Unknown;
            ParentTasks = new Stack<long>();
        }

        public TaskExecution Enqueue()
        {
            Runner.Enqueue(this);
            return this;
        }

        public TaskExecution Wait() { done.Wait(); return this; }
        public bool Wait(TimeSpan timeout) => done.Wait(timeout);
        
        public async System.Threading.Tasks.Task<TaskExecution> WaitAsync() 
        {
            if (done_cts.Token.IsCancellationRequested)
                return this;
            await System.Threading.Tasks.Task.Delay(-1, done_cts.Token).ContinueWith(t => {}); return this; 
        }
        public async System.Threading.Tasks.Task<TaskExecution> WaitAsync(TimeSpan timeout) { await System.Threading.Tasks.Task.Delay(timeout, done_cts.Token).ContinueWith(t => {}); return this; }
        internal TaskResult ExecuteBlocking(Task other, TaskContext ctx) => ExecuteBlocking(Runner.Register(other, ctx));
        internal TaskResult ExecuteBlocking(TaskExecution exec)
        {
            exec.ParentTasks.Push(Task.Identifier);
            foreach (var parent in ParentTasks)
                exec.ParentTasks.Push(parent);
            
            return Runner.ExecuteBlocking(exec);
        }
        internal async System.Threading.Tasks.Task<TaskResult> ExecuteAsync(Task other, TaskContext ctx) => await ExecuteAsync(Runner.Register(other, ctx));
        internal async System.Threading.Tasks.Task<TaskResult> ExecuteAsync(TaskExecution exec)
        {
            exec.ParentTasks.Push(Task.Identifier);
            foreach (var parent in ParentTasks)
                exec.ParentTasks.Push(parent);
            
            return await Runner.ExecuteAsync(exec);
        }

        internal void SignalDone()
        {
            done.Set();
            done_cts.Cancel();
        }

        public override string ToString()
        {
            return $"{string.Join('/', ParentTasks.Append(Task.Identifier))}:{Task.Type}";
        }
    }

    public class TaskRunner
    {
        Logger Log = LogManager.GetCurrentClassLogger();
        public Dictionary<long, Task> Tasks = new Dictionary<long, Task>();
        public Dictionary<long, TaskExecution> Executions = new Dictionary<long, TaskExecution>();

        public ConcurrentDictionary<long, long> YieldedExecutions = new ConcurrentDictionary<long, long>();
        public ConcurrentQueue<TaskExecution> TaskQueue = new ConcurrentQueue<TaskExecution>();

        public ServiceManager ServiceManager { get; private set; }

        private Random random = new Random();
        private HashSet<long> served_ids = new HashSet<long>();
        private CancellationTokenSource cts = new CancellationTokenSource();

        public TaskRunner(ServiceManager manager)
        {
            ServiceManager = manager;
        }

        int executed = 0;

        public async void Run()
        {
            while (true)
            {
                TaskExecution execution;
                while (!TaskQueue.TryDequeue(out execution))
                {
                    Thread.Sleep(10);
                }

                if (execution == null || execution.Task == null || execution.Runner != this || execution.State != TaskExecutionState.Enqueued)
                {
                    Log.Warn($"Skipping wrong task execution");
                    continue;
                }

                try 
                {
                    await ExecuteAsync(execution);
                }
                catch (Exception ex)
                {
                    Log.Error(ex);
                    execution.State = TaskExecutionState.Aborted;
                    execution.done.Set();
                }
            }
        }

        long last_served_id = 0;
        static readonly bool RandomTaskIdentifiers = false;
        static readonly bool KeepTasks = false;

        public long GetNewIdentifier()
        {
            lock (served_ids)
            {
                long id = 0;

                if (RandomTaskIdentifiers)
                {
                    var id_bytes = new byte[8];

                    while (served_ids.Contains(BitConverter.ToInt64(id_bytes)))
                    {
                        random.NextBytes(id_bytes);
                        
                        for (int i = 0; i < 6; i++)
                            id_bytes[7 - i] = 0;
                    }
                    
                    id = BitConverter.ToInt64(id_bytes);
                }
                else
                {
                    while (served_ids.Contains(id))
                        id = last_served_id++;
                }

                served_ids.Add(id);
                return id;
            }
        }

        internal async System.Threading.Tasks.Task<TaskResult> ExecuteAsync(Task task, TaskContext context) => await ExecuteAsync(Register(task, context));

        internal async System.Threading.Tasks.Task<TaskResult> ExecuteAsync(TaskExecution execution)
        {
            if (execution == null || execution.Task == null || execution.Runner != this || 
               (execution.State != TaskExecutionState.Enqueued && execution.State != TaskExecutionState.Registered))
            {
                Log.Error($"Invalid task execution");
                return new TaskResult(execution?.Task, ResultType.Failure, "Invalid task execution");
            }

            try 
            {
                execution.State = TaskExecutionState.Executing;

                try 
                {
                    if (execution.Task is AsyncTask async_task)
                    {
                        execution.Result = await async_task.ExecuteAsync(execution.Context);
                    }
                    else
                        execution.Result = execution.Task.Execute(execution.Context); 
                }
                catch (Exception ex)
                {
                    execution.Result = new TaskResult(execution.Task, ResultType.Failure, ex);
                    Log.Error(ex);
                }
                finally
                {
                    executed++;
                    execution.SignalDone();
                }

                execution.SignalDone();
            }
            catch (Exception ex)
            {
                Log.Error(ex);
                execution.State = TaskExecutionState.Aborted;
                execution.SignalDone();
            }

            if (!KeepTasks && Tasks.ContainsKey(execution.Task.Identifier))
            {
                Tasks.Remove(execution.Task.Identifier);
            }

            return execution.Result;
        }

        internal TaskResult ExecuteBlocking(Task task, TaskContext context = null) => ExecuteBlocking(this.Register(task, context));

        internal TaskResult ExecuteBlocking(TaskExecution execution) => ExecuteAsync(execution).Result;

        public TaskExecution Register(Task task, TaskContext context = null)
        {
            if (task.Runner == this)
                return task.Execution;

            if (task.Execution != null)
                throw new Exception($"Cannot re-register task.");

            if (task.Identifier == 0)
                task.Identifier = GetNewIdentifier();
            
            lock (Tasks)
            {
                Tasks[task.Identifier] = task;
            }

            var exec = new TaskExecution(this, task, context);
            exec.State = TaskExecutionState.Registered;

            if (exec.Context == null)
                exec.Context = new TaskContext();
            
            task.Execution = exec;
            return exec;
        }

        internal void Enqueue(TaskExecution exec)
        {
            if (exec.State != TaskExecutionState.Registered)
            {
                Log.Warn($"Refusing to enqueue task execution with state {exec.State}");
                return;
            }

            Log.Debug($"{exec.Task} enqueued");
            exec.State = TaskExecutionState.Enqueued;
            TaskQueue.Enqueue(exec);
        }
    }
}