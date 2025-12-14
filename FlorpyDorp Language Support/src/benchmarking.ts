/**
 * Comprehensive Benchmarking System for IC10 Extension
 * 
 * Tracks performance metrics across:
 * - LSP operations (hover, completion, diagnostics)
 * - Extension middleware
 * - Document processing
 * - Tree-sitter parsing (via LSP)
 */

import * as vscode from 'vscode';

export class PerformanceTracker {
    private measurements: Map<string, number[]> = new Map();
    private counters: Map<string, number> = new Map();
    private startTimes: Map<string, number> = new Map();
    
    /**
     * Start timing an operation
     */
    start(operationName: string): void {
        this.startTimes.set(operationName, performance.now());
    }
    
    /**
     * End timing and record duration
     */
    end(operationName: string): number {
        const startTime = this.startTimes.get(operationName);
        if (!startTime) {
            console.warn(`[Perf] No start time found for ${operationName}`);
            return 0;
        }
        
        const duration = performance.now() - startTime;
        this.record(operationName, duration);
        this.startTimes.delete(operationName);
        return duration;
    }
    
    /**
     * Record a measurement
     */
    record(operationName: string, durationMs: number): void {
        if (!this.measurements.has(operationName)) {
            this.measurements.set(operationName, []);
        }
        
        const measurements = this.measurements.get(operationName)!;
        measurements.push(durationMs);
        
        // Keep only last 1000 measurements to prevent memory growth
        if (measurements.length > 1000) {
            measurements.shift();
        }
    }
    
    /**
     * Increment a counter
     */
    increment(counterName: string, amount: number = 1): void {
        const current = this.counters.get(counterName) || 0;
        this.counters.set(counterName, current + amount);
    }
    
    /**
     * Get statistics for an operation
     */
    getStats(operationName: string): OperationStats | null {
        const measurements = this.measurements.get(operationName);
        if (!measurements || measurements.length === 0) {
            return null;
        }
        
        const sorted = [...measurements].sort((a, b) => a - b);
        const total = measurements.reduce((a, b) => a + b, 0);
        const count = measurements.length;
        
        return {
            count,
            total,
            avg: total / count,
            min: sorted[0],
            max: sorted[sorted.length - 1],
            p50: sorted[Math.floor(count * 0.5)],
            p95: sorted[Math.floor(count * 0.95)],
            p99: sorted[Math.floor(count * 0.99)]
        };
    }
    
    /**
     * Get all statistics
     */
    getAllStats(): Map<string, OperationStats> {
        const stats = new Map<string, OperationStats>();
        for (const [name, _] of this.measurements) {
            const stat = this.getStats(name);
            if (stat) {
                stats.set(name, stat);
            }
        }
        return stats;
    }
    
    /**
     * Get all counters
     */
    getAllCounters(): Map<string, number> {
        return new Map(this.counters);
    }
    
    /**
     * Reset all statistics
     */
    reset(): void {
        this.measurements.clear();
        this.counters.clear();
        this.startTimes.clear();
    }
    
    /**
     * Generate a formatted report
     */
    generateReport(): string {
        const lines: string[] = [];
        lines.push('='.repeat(80));
        lines.push('IC10 Extension Performance Report');
        lines.push('='.repeat(80));
        lines.push('');
        
        // Timing Statistics
        lines.push('TIMING STATISTICS');
        lines.push('-'.repeat(80));
        
        const stats = this.getAllStats();
        if (stats.size === 0) {
            lines.push('  No timing data collected');
        } else {
            // Group by category
            const categories = new Map<string, Array<[string, OperationStats]>>();
            
            for (const [name, stat] of stats) {
                const category = name.split('.')[0];
                if (!categories.has(category)) {
                    categories.set(category, []);
                }
                categories.get(category)!.push([name, stat]);
            }
            
            // Sort categories
            const sortedCategories = Array.from(categories.entries()).sort((a, b) => 
                a[0].localeCompare(b[0])
            );
            
            for (const [category, operations] of sortedCategories) {
                lines.push('');
                lines.push(`${category.toUpperCase()}:`);
                
                // Sort operations by total time (descending)
                operations.sort((a, b) => b[1].total - a[1].total);
                
                for (const [name, stat] of operations) {
                    const shortName = name.substring(category.length + 1);
                    lines.push(`  ${shortName || name}:`);
                    lines.push(`    Calls:    ${stat.count}`);
                    lines.push(`    Total:    ${stat.total.toFixed(2)}ms`);
                    lines.push(`    Avg:      ${stat.avg.toFixed(2)}ms`);
                    lines.push(`    Min:      ${stat.min.toFixed(2)}ms`);
                    lines.push(`    Max:      ${stat.max.toFixed(2)}ms`);
                    lines.push(`    P50:      ${stat.p50.toFixed(2)}ms`);
                    lines.push(`    P95:      ${stat.p95.toFixed(2)}ms`);
                    lines.push(`    P99:      ${stat.p99.toFixed(2)}ms`);
                }
            }
        }
        
        lines.push('');
        lines.push('COUNTERS');
        lines.push('-'.repeat(80));
        
        const counters = this.getAllCounters();
        if (counters.size === 0) {
            lines.push('  No counter data collected');
        } else {
            const sortedCounters = Array.from(counters.entries()).sort((a, b) => 
                a[0].localeCompare(b[0])
            );
            
            for (const [name, value] of sortedCounters) {
                lines.push(`  ${name}: ${value}`);
            }
        }
        
        lines.push('');
        lines.push('='.repeat(80));
        
        return lines.join('\n');
    }
}

export interface OperationStats {
    count: number;
    total: number;
    avg: number;
    min: number;
    max: number;
    p50: number;  // Median
    p95: number;  // 95th percentile
    p99: number;  // 99th percentile
}

// Global singleton instance
let globalTracker: PerformanceTracker | null = null;

export function getGlobalTracker(): PerformanceTracker {
    if (!globalTracker) {
        globalTracker = new PerformanceTracker();
    }
    return globalTracker;
}

export function resetGlobalTracker(): void {
    globalTracker = new PerformanceTracker();
}

/**
 * Decorator for automatic timing of async functions
 */
export function timed(operationName: string) {
    return function (target: any, propertyKey: string, descriptor: PropertyDescriptor) {
        const originalMethod = descriptor.value;
        
        descriptor.value = async function (...args: any[]) {
            const tracker = getGlobalTracker();
            tracker.start(operationName);
            try {
                const result = await originalMethod.apply(this, args);
                return result;
            } finally {
                tracker.end(operationName);
            }
        };
        
        return descriptor;
    };
}

/**
 * Higher-order function for timing operations
 */
export function withTiming<T>(operationName: string, fn: () => T | Promise<T>): Promise<T> {
    const tracker = getGlobalTracker();
    tracker.start(operationName);
    
    try {
        const result = fn();
        if (result instanceof Promise) {
            return result.finally(() => tracker.end(operationName));
        } else {
            tracker.end(operationName);
            return Promise.resolve(result);
        }
    } catch (error) {
        tracker.end(operationName);
        throw error;
    }
}
