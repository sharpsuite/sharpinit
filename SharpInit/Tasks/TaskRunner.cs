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
        internal TaskResult YieldExecute(Task other, TaskContext ctx) => YieldExecute(Runner.Register(other, ctx));
        internal TaskResult YieldExecute(TaskExecution exec)
        {
            exec.ParentTasks.Push(Task.Identifier);
            foreach (var parent in ParentTasks)
                exec.ParentTasks.Push(parent);
            
            return Runner.ExecuteBlocking(exec);
        }

        public override string ToString()
        {
            return $"{string.Join('/', ParentTasks)}/{Task.Identifier}:{Task.Type}";
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

        public void Run()
        {
            while (true)
            {
                Log.Info($"{TaskQueue.Count} tasks in queue, {executed} executed");

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
                    ExecuteBlocking(execution);
                }
                catch (Exception ex)
                {
                    Log.Error(ex);
                    execution.State = TaskExecutionState.Aborted;
                    execution.done.Set();
                }
            }
        }

        public long GetNewIdentifier()
        {
            lock (served_ids)
            {
                var id_bytes = new byte[8];

                while (served_ids.Contains(BitConverter.ToInt64(id_bytes)))
                {
                    random.NextBytes(id_bytes);
                    
                    for (int i = 0; i < 6; i++)
                        id_bytes[7 - i] = 0;
                }
                
                var id = BitConverter.ToInt64(id_bytes);
                served_ids.Add(id);
                return id;
            }
        }

        internal TaskResult ExecuteBlocking(Task task, TaskContext context = null) => ExecuteBlocking(this.Register(task, context));

        internal TaskResult ExecuteBlocking(TaskExecution execution)
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
                Log.Info($"{execution} starting...");

                try 
                {
                    execution.Result = execution.Task.Execute(execution.Context); 
                }
                catch (Exception ex)
                {
                    execution.Result = new TaskResult(execution.Task, ResultType.Failure, ex.Message);
                    Log.Error(ex);
                }
                finally
                {
                    executed++;
                    execution.done.Set();
                }

                Log.Info($"{execution} done");
                execution.done.Set();
            }
            catch (Exception ex)
            {
                Log.Error(ex);
                execution.State = TaskExecutionState.Aborted;
                execution.done.Set();
            }

            return execution.Result;
        }

        public TaskExecution Register(Task task, TaskContext context = null)
        {
            if (task.Runner == this)
                return task.Execution;

            if (task.Execution != null)
                throw new Exception($"Cannot re-register task.");

            if (task.Identifier == 0)
                task.Identifier = GetNewIdentifier();

            Tasks[task.Identifier] = task;

            var exec = new TaskExecution(this, task, context);
            exec.State = TaskExecutionState.Registered;
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

            Log.Info($"{exec.Task} enqueued");
            exec.State = TaskExecutionState.Enqueued;
            TaskQueue.Enqueue(exec);
        }
    }
}