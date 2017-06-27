using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Threading;
using System.Threading.Tasks;

namespace ThreadingExamples
{
    /// <summary>
    /// a bunch of threading examples.... focused on how the TM/client/mynextep are threading right now.
    /// </summary>
    public partial class MainWindow : Window
    {
        private System.Windows.Threading.Dispatcher _uiDispatcher = null;

        public MainWindow()
        {
            InitializeComponent();

            _uiDispatcher = this.Dispatcher;
            btnTimerStop.IsEnabled = false;
            btnTimeDispatchStop.IsEnabled = false;
        }

        #region timers

        /// <summary>
        /// simple timer example. the TM uses a few timers. good for firing off work async on an interval
        /// CATCH: the function always fires even if the prior run is still executing.
        ///         recommend using a monitory.TryEnter(...) to ensure that calls don't stack up
        /// </summary>
        private Timer _timer;
        private ThreadPayload _timerPayload;
        private void btnTimerStart_Click(object sender, RoutedEventArgs e)
        {
            btnTimerStart.IsEnabled = false;
            btnTimerStop.IsEnabled = true;
            Log("start timer");
            _timerPayload = new ThreadPayload();
            _timerPayload.Message = "Timer Thread Payload";
            _timerPayload.Delay = int.Parse(txtWorkTime.Text);
            _timer = new Timer(DoTimerFired, _timerPayload, 1000, 1000);
        }

        private void btnTimerStop_Click(object sender, RoutedEventArgs e)
        {
            Log("stop timer");
            if (_timerPayload != null)
            {
                _timer.Change(Timeout.Infinite, Timeout.Infinite);
                _timer.Dispose();
                _timer = null;
            }
            btnTimerStart.IsEnabled = true;
            btnTimerStop.IsEnabled = false;
        }

        private static object _timerLock = new object();

        private void DoTimerFired(object state)
        {
            //Monitor.Enter(_timerLock)//blocking...

            if (Monitor.TryEnter(_timerLock))
            {
                try
                {
                    //TODO: sync up this timer... add a monitor.try or a lock
                    ThreadPayload payload = (ThreadPayload)state;
                    payload.Count++;
                    Log("starting timer work");
                    Thread.Sleep(payload.Delay);
                    Log(payload);
                    Log("finished timer work");
                }
                finally
                {
                    Monitor.Exit(_timerLock);
                }
            }
            else
            {
                Log("timer already doing work... skipping payload");
            }
        }


        /// <summary>
        /// dispatcher timer is already sync'd with the UI thread.
        /// so any action taking on it's Tick() method can safely access UI objects in WPF
        /// </summary>
        System.Windows.Threading.DispatcherTimer _dTimer;
        private ThreadPayload _dTimerPayload;

        private void btnTimeDispatchStart_Click(object sender, RoutedEventArgs e)
        {
            btnTimeDispatchStart.IsEnabled = false;
            btnTimeDispatchStop.IsEnabled = true;
            Log("start dispatch timer");
            _dTimerPayload = new ThreadPayload();
            _dTimerPayload.Message = "Dispatch Timer Thread Payload";
            _dTimerPayload.Delay = int.Parse(txtWorkTime.Text);
            _dTimer = new System.Windows.Threading.DispatcherTimer();
            _dTimer.Tick += _dTimer_Tick;
            _dTimer.Interval = new TimeSpan(0,0,0,1,0);
            _dTimer.Start();
        }

        private void btnTimeDispatchStop_Click(object sender, RoutedEventArgs e)
        {
            Log("stop dispatch timer");
            if (_dTimer != null)
            {
                _dTimer.Stop();
                _dTimer = null;
            }
            btnTimeDispatchStart.IsEnabled = true;
            btnTimeDispatchStop.IsEnabled = false;
        }


        private void _dTimer_Tick(object sender, EventArgs e)
        {
            _dTimerPayload.Count++;

            //will lock the UI
            //Thread.Sleep(_dTimerPayload.Delay);

            Log(_dTimerPayload);

            //TODO: dispatcher timer can access the UI thread cause it is sync'd with the UI thread
            lblDispatcherLabel.Content = $"Dispatcher! {_dTimerPayload.Count}";
            
        }

        #endregion

        #region Logging
        private void Log(ThreadPayload payload)
        {
            string msg = $"{payload.Message}, Payload id: {payload.PayloadId.ToString()}, Count: {payload.Count}";
            Log(msg);
        }

        private static object _logLock = new object();

        private void Log(string msg)
        {
            string logmsg = DateTime.Now.ToString("HH:mm:ss.fff") + "\t" + msg;

            //not UI thread safe....BOOM
            //loggingView.Items.Add(logmsg);

            //thread safe... no BOOM
            ExecuteOnUIThreadAsync(new Action(() =>
            {
                lock (_logLock)
                {
                    loggingView.Items.Add(logmsg);
                }
            }));
        }

        private void btnClear_Click(object sender, RoutedEventArgs e)
        {
            loggingView.Items.Clear();
        }

        #endregion

        #region UI threading
        /// <summary>
        /// Executes code in the UI thread and returns (no blocking!)
        /// code is execute async on the ui thread and this thread continues on.
        /// The exception is if this thread is the Ui thread, then it blocks
        /// </summary>
        public void ExecuteOnUIThreadAsync(Action action)
        {
            if (_uiDispatcher.CheckAccess())
            {
                action();
            }
            else
            {
                _uiDispatcher.BeginInvoke(action);
            }
        }

        #endregion

        #region thread pool queing

        /// <summary>
        /// throw a task on the threadpool... for async processing. pretty simple
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void btnThreadQueue_Click(object sender, RoutedEventArgs e)
        {
            ThreadPayload payload = new ThreadPayload()
            {
                Delay = int.Parse(txtWorkTime.Text),
                Message = "Thread Queue"
            };
            
            //this call will not block and will return right away. 
            ThreadPool.QueueUserWorkItem(DoQueuedWork, payload);
        }


        private void DoQueuedWork(object state)
        {
            ThreadPayload payload = (ThreadPayload)state;
            while (payload.Count < 10)
            {
                Log(payload);
                Thread.Sleep(payload.Delay);
                payload.Count++;
            }
        }

        #endregion

        #region creating threads directly
        /// <summary>
        /// below is an example of created threads directly.
        /// remember to always name the thread and think about the IsbAckground property
        /// And threads directly created should be tracked (keep a reference to it) so that it can
        /// be properly stopped/cleaned up as needed
        /// </summary>
        ThreadPayload _sharedThreadPayload = null;
        int _threadCount = 0;
        Dictionary<string, Thread> _startedThreads = new Dictionary<string, Thread>();

        private void btnThreadStartUp_Click(object sender, RoutedEventArgs e)
        {
            Log("Starting new Thread");
            if (_sharedThreadPayload == null)
            {
                _sharedThreadPayload = new ThreadPayload()
                {
                    Message = "Shared Thread Payload",
                    Delay = int.Parse(txtWorkTime.Text),
                    NumLoops = int.Parse(txtNumLoops.Text)
                };
            }

            Thread thread = new Thread(DoThreadWork);
            thread.Name = "Shared Thread " + (_threadCount++).ToString(); //always give your threads name! makes debugging easier
            //TODO: show effect of this
            thread.IsBackground = true; //set to background so it doesn't hav eto be killed for the app to close
            
            //create a button per thread that can be used to stop the thread before it is complete
            Button b = new Button();
            b.Name = "btnThread" + _threadCount.ToString();
            b.Content = "Stop: " + thread.Name;
            b.Click += B_Click;
            b.Height = 40;
            threadButtonStack.Children.Add(b);

            //keep track of the thread and start it
            _startedThreads.Add(b.Name, thread);
            thread.Start();
        }

        /// <summary>
        /// fired with the thread stop button is hit. will stop the thread if it is running
        /// </summary>
        private void B_Click(object sender, RoutedEventArgs e)
        {
            Button b = (Button)sender;
            Thread t = _startedThreads[b.Name];

            Log("exiting thread: " + t.Name);

            if (t.IsAlive)
            {
                if (chkJoin.IsChecked != null && chkJoin.IsChecked.Value)
                {
                    // will block until timeout is reach or thread exits on its own
                    t.Join(5000);
                }
                else
                {
                    t.Abort();
                }
            }

            threadButtonStack.Children.Remove(b);
            _startedThreads.Remove(b.Name);
            Log("done exiting thread: " + t.Name);
        }

        /// <summary>
        /// the actual method doing the work on the thread created in this section. 
        /// </summary>
        private void DoThreadWork()
        {
            try
            {
                Log(Thread.CurrentThread.Name + " started on shared payload");
                int count = 0;
                while(count < _sharedThreadPayload.NumLoops)
                {
                    count++;
                    Log(Thread.CurrentThread.Name + " doing work. count: " + count.ToString());
                    Thread.Sleep(_sharedThreadPayload.Delay);
                }
                Log(Thread.CurrentThread.Name + " finished");

            }
            catch (ThreadAbortException e)
            {
                Log("thread abort exception fired");
            }
        }


        #endregion

        #region Creating a worker thread and signaling it to do work

        /// <summary>
        /// this is the queue of work that will be loaded and then processed by the worker thread
        /// </summary>
        private Queue<ThreadPayload> _workerQueue = new Queue<ThreadPayload>();

        /// <summary>
        /// the one and only worker thread that processes that queue
        /// </summary>
        private Thread _workerThread;

        /// <summary>
        /// used to signal the worker thread to do work and process the queue
        /// </summary>
        private AutoResetEvent _workAvailable;

        /// <summary>
        /// uyse to sync up access to the resources the worker thread uses
        /// </summary>
        private static object _workerLock = new object();


        private void btnStartWorkerThread_Click(object sender, RoutedEventArgs e)
        {
            //start up the worker thread
            if (_workerThread == null)
            {
                _workAvailable = new AutoResetEvent(false);

                _workerThread = new Thread(DoWorkerThread);
                _workerThread.Name = "Worker Thread";
                //TODO: not setting to true this would cause the app to hang until worker thread exits
                _workerThread.IsBackground = true; 
                _workerThread.Start();
            }
            else
            {
                Log("worker thread is already going");
            }
        }

        private void btnQueueUpWork_Click(object sender, RoutedEventArgs e)
        {
            //queue up some work for that worker thread
            int num = int.Parse(txtAmountOfWork.Text);
            Log($"Adding {num} payload(s) to queue.");

            for (int i = 0; i < num; i++)
            {
                ThreadPayload payload = new ThreadPayload()
                {
                    Message = "Worker payload",
                    Delay = int.Parse(txtWorkTime.Text),
                    NumLoops = int.Parse(txtNumLoops.Text)
                };

                //no locking... will just add to the queue. 
                //_workerQueue.Enqueue(payload);

                /*
                lock (_workerLock) //will lock UI and wait for the queue to be available
                {
                    _workerQueue.Enqueue(payload);
                }
                */

                //if the working is working then will skip adding work
                if (Monitor.TryEnter(_workerLock))
                {
                    try
                    {
                        _workerQueue.Enqueue(payload);
                    }
                    finally
                    {
                        //super important! must always release a lock! 
                        Monitor.Exit(_workerLock);
                    }
                }
                else
                {
                    Log("Can't give the worker more work because it is still consuming");
                }
            }
        }

        private void btnTellThreadToConsume_Click(object sender, RoutedEventArgs e)
        {
            _workAvailable.Set(); //signal that tehre is work! 
        }

        /// <summary>
        /// the worker thread's method
        /// </summary>
        private void DoWorkerThread()
        {
            while(true) //just look forever...
            {
                Log("Worker thread is waiting for work");
                if (_workAvailable.WaitOne()) //wait until signaled
                {
                    Log("Check for work signaled!");
                    lock (_workerLock)
                    {
                        //burn through the available work
                        Log("current work count: " + _workerQueue.Count.ToString());
                        while (_workerQueue.Any())
                        {
                            ThreadPayload payload = _workerQueue.Dequeue();
                            Log(payload);
                            Thread.Sleep(payload.Delay);
                        }

                        Log("All work complete");
                    }
                }
            }
        }

        /// <summary>
        /// stop thw worker thread
        /// </summary>
        private void btnStopWorkerThread_Click(object sender, RoutedEventArgs e)
        {
            if (_workerThread != null)
            {
                Log("stoping working thread");

                //_workerThread.Join(); //will wait forever... would be bad

                _workerThread.Abort();
                _workerThread = null;
            }
            else
            {
                Log("worker thread is not running");
            }
        }

        #endregion

        #region task namespace primer
        private void btnParallel_Click(object sender, RoutedEventArgs e)
        {
            /*
            Log("Starting parallel Loop");

            lock (_workerLock)
            {
                //this will block the UI...
                Parallel.ForEach(_workerQueue, x =>
                {
                    Log(x);
                    Thread.Sleep(x.Delay);
                });

                _workerQueue.Clear();
            }

            Log("parallel Loop complete");
            */

            //combine stuff and start parrallel loop on the thread pool! crazy
            ThreadPool.QueueUserWorkItem(DoParallelLoop, null);
        }


        private void DoParallelLoop(object state)
        {
            Log("Starting parallel Loop on threadpool");
            lock (_workerLock)
            {
                Parallel.ForEach(_workerQueue, x =>
                {
                    Log(x);
                    Thread.Sleep(x.Delay);
                });

                _workerQueue.Clear();
            }

            Log("parallel Loop complete on threadpool");
        }

        private void btnTask_Click(object sender, RoutedEventArgs e)
        {
            Log("Starting the async task");
            //this is sort of like calling the ThreadPool.QueueUserWorkItem... will call async
            Task t = Task.Factory.StartNew(() => {

                lock (_workerLock)
                {
                    //burn through the work load
                    while (_workerQueue.Any())
                    {
                        ThreadPayload payload = _workerQueue.Dequeue();
                        Log(payload);
                        Thread.Sleep(payload.Delay);
                    }
                }

                Log("async task complete");
            });

            //TODO: lots ollf stuff to do with tasks...
            //t.Wait(); //this will block the UI, but useful for synchronizing 
        }
        
        #endregion
    }
}
