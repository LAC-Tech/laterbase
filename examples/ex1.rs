fn main() {
	let mut node1 = laterbase::Node::new();
	let mut node2 = laterbase::Node::new();

	node2.add_local(vec![127, 63]);
	node1.merge(&mut node2);

	println!("example running");
}


