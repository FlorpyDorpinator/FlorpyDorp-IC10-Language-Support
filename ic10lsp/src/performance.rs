//! Performance benchmarking module for ic10lsp
//! 
//! Provides timing and statistics tracking for LSP operations

use std::collections::HashMap;
use std::sync::{Arc, Mutex};
use std::time::{Duration, Instant};

#[derive(Clone)]
pub struct PerformanceTracker {
    measurements: Arc<Mutex<HashMap<String, Vec<Duration>>>>,
    counters: Arc<Mutex<HashMap<String, u64>>>,
    enabled: Arc<Mutex<bool>>,
}

impl PerformanceTracker {
    pub fn new() -> Self {
        Self {
            measurements: Arc::new(Mutex::new(HashMap::new())),
            counters: Arc::new(Mutex::new(HashMap::new())),
            enabled: Arc::new(Mutex::new(false)),
        }
    }
    
    pub fn is_enabled(&self) -> bool {
        *self.enabled.lock().unwrap()
    }
    
    pub fn set_enabled(&self, enabled: bool) {
        *self.enabled.lock().unwrap() = enabled;
        if enabled {
            self.reset();
        }
    }
    
    pub fn record(&self, operation: &str, duration: Duration) {
        if !self.is_enabled() {
            return;
        }
        
        let mut measurements = self.measurements.lock().unwrap();
        measurements
            .entry(operation.to_string())
            .or_insert_with(Vec::new)
            .push(duration);
    }
    
    pub fn increment(&self, counter: &str, amount: u64) {
        if !self.is_enabled() {
            return;
        }
        
        let mut counters = self.counters.lock().unwrap();
        *counters.entry(counter.to_string()).or_insert(0) += amount;
    }
    
    pub fn reset(&self) {
        self.measurements.lock().unwrap().clear();
        self.counters.lock().unwrap().clear();
    }
    
    pub fn generate_report(&self) -> String {
        let measurements = self.measurements.lock().unwrap();
        let counters = self.counters.lock().unwrap();
        
        let mut report = String::new();
        report.push_str("=".repeat(80).as_str());
        report.push_str("\nIC10 LSP Server Performance Report\n");
        report.push_str("=".repeat(80).as_str());
        report.push_str("\n\n");
        
        // Timing Statistics
        report.push_str("TIMING STATISTICS\n");
        report.push_str("-".repeat(80).as_str());
        report.push_str("\n");
        
        if measurements.is_empty() {
            report.push_str("  No timing data collected\n");
        } else {
            let mut ops: Vec<_> = measurements.iter().collect();
            ops.sort_by_key(|(name, _)| *name);
            
            for (operation, times) in ops {
                if times.is_empty() {
                    continue;
                }
                
                let count = times.len();
                let total: Duration = times.iter().sum();
                let avg = total / count as u32;
                let min = times.iter().min().unwrap();
                let max = times.iter().max().unwrap();
                
                let mut sorted = times.clone();
                sorted.sort();
                let p50 = sorted[count / 2];
                let p95 = sorted[(count as f64 * 0.95) as usize];
                let p99 = sorted[(count as f64 * 0.99) as usize];
                
                report.push_str(&format!("\n  {}:\n", operation));
                report.push_str(&format!("    Calls:    {}\n", count));
                report.push_str(&format!("    Total:    {:.2}ms\n", total.as_secs_f64() * 1000.0));
                report.push_str(&format!("    Avg:      {:.2}ms\n", avg.as_secs_f64() * 1000.0));
                report.push_str(&format!("    Min:      {:.2}ms\n", min.as_secs_f64() * 1000.0));
                report.push_str(&format!("    Max:      {:.2}ms\n", max.as_secs_f64() * 1000.0));
                report.push_str(&format!("    P50:      {:.2}ms\n", p50.as_secs_f64() * 1000.0));
                report.push_str(&format!("    P95:      {:.2}ms\n", p95.as_secs_f64() * 1000.0));
                report.push_str(&format!("    P99:      {:.2}ms\n", p99.as_secs_f64() * 1000.0));
            }
        }
        
        // Counters
        report.push_str("\n\nCOUNTERS\n");
        report.push_str("-".repeat(80).as_str());
        report.push_str("\n");
        
        if counters.is_empty() {
            report.push_str("  No counter data collected\n");
        } else {
            let mut items: Vec<_> = counters.iter().collect();
            items.sort_by_key(|(name, _)| *name);
            
            for (name, value) in items {
                report.push_str(&format!("  {}: {}\n", name, value));
            }
        }
        
        report.push_str("\n");
        report.push_str("=".repeat(80).as_str());
        report.push_str("\n");
        
        report
    }
}

impl Default for PerformanceTracker {
    fn default() -> Self {
        Self::new()
    }
}

/// RAII guard for automatic timing
pub struct TimingGuard {
    tracker: PerformanceTracker,
    operation: String,
    start: Instant,
}

impl TimingGuard {
    pub fn new(tracker: &PerformanceTracker, operation: impl Into<String>) -> Self {
        Self {
            tracker: tracker.clone(),
            operation: operation.into(),
            start: Instant::now(),
        }
    }
}

impl Drop for TimingGuard {
    fn drop(&mut self) {
        let duration = self.start.elapsed();
        self.tracker.record(&self.operation, duration);
    }
}

/// Macro for easy timing
#[macro_export]
macro_rules! time_operation {
    ($tracker:expr, $operation:expr, $block:block) => {{
        let _guard = $crate::performance::TimingGuard::new($tracker, $operation);
        $block
    }};
}
