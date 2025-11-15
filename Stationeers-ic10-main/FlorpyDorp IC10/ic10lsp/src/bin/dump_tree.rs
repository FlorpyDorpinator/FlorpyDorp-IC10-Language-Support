use tree_sitter::{Node, Parser};

fn print_node(node: Node, source: &[u8], depth: usize) {
    let indent = "  ".repeat(depth);
    let text = &source[node.start_byte()..node.end_byte()];
    let snippet = std::str::from_utf8(text).unwrap_or("");
    let one_line = snippet.lines().next().unwrap_or("");
    println!(
        "{indent}{} @ {:?}..{:?} -> {}",
        node.kind(),
        node.start_position(),
        node.end_position(),
        one_line
    );
    let mut cursor = node.walk();
    for child in node.children(&mut cursor) {
        print_node(child, source, depth + 1);
    }
}

fn main() {
    let path = std::env::args().nth(1).expect("usage: dump_tree <file>");
    let content = std::fs::read_to_string(&path).expect("read file");
    let mut parser = Parser::new();
    parser
        .set_language(tree_sitter_ic10::language())
        .expect("language");
    let tree = parser.parse(&content, None).expect("parse");
    let root = tree.root_node();
    print_node(root, content.as_bytes(), 0);
}
