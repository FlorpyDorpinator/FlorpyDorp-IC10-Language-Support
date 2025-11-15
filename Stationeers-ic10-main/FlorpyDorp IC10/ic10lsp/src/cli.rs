use clap::Parser;

#[derive(Parser, Debug)]
#[command(version)]
pub(crate) struct Cli {
    #[arg(long)]
    pub listen: bool,
    #[arg(long)]
    pub host: Option<String>,
    #[arg(long)]
    pub port: Option<u16>,
    /// Run diagnostics on the provided files and print results to stdout
    #[arg(long)]
    pub diagnose: Vec<std::path::PathBuf>,
}
