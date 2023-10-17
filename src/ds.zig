// Data Structures

const std = @import("std");

// Binary Search Tree
// Implementing this because Zig doesn't have kind of sorted map
// Also, this is the simplest one I could think of

// TODO: "Performance Analysis of BSTs in System Software" (Pfaff, 2003)
// Paper suggets Splay trees or what we want
// AVL trees are second best, and do not modify tree when searching
pub fn BST(
    comptime K: type,
    comptime V: type,
    comptime compare: anytype,
) type {
    const Node = struct {
        key: K,
        val: V,
        left: ?*@This(),
        right: ?*@This(),

        const Self = @This();

        const Next = union(enum) {
            left: *Self,
            right: *Self,
            left_null: void,
            right_null: void,
            end: void,
        };

        fn next(self: @This(), k: K) Next {
            switch (compare(self.key, k)) {
                .gt => if (self.left) |left_node| {
                    return .{ .left = left_node };
                } else {
                    return .{ .left_null = {} };
                },
                .lt => if (self.right) |right_node| {
                    return .{ .right = right_node };
                } else {
                    return .{ .right_null = {} };
                },
                .eq => return .{ .end = {} },
            }
        }
    };

    const FoundNode = union(enum) {
        first_gt: *Node,
        eq: *Node,
        not_found: void,
    };

    return struct {
        root: ?*Node,
        len: usize,
        allocator: std.heap.MemoryPool(Node),

        const Self = @This();
        const Entry = struct { key_ptr: *K, value_ptr: *V };
        const ParentStack = std.ArrayListUnmanaged(?*Node);

        const Iterator = struct {
            parent_stack: ParentStack,
            current: ?*Node,

            pub fn init(
                allocator: std.mem.Allocator,
                bst: Self,
            ) !@This() {
                var parent_stack = try ParentStack.initCapacity(
                    allocator,
                    bst.len,
                );
                var current = bst.root;

                for (0..bst.len) |_| {
                    if (current) |c| {
                        if (c.left) |left| {
                            try parent_stack.append(allocator, current);
                            current = left;
                        }
                    } else {
                        break;
                    }
                }

                return .{
                    .parent_stack = parent_stack,
                    .current = current,
                };
            }

            pub fn initFrom(
                allocator: std.mem.Allocator,
                bst: Self,
                start_key: K,
            ) !@This() {
                var parent_stack = try ParentStack.initCapacity(
                    allocator,
                    bst.len,
                );

                if (bst.root) |initial| {
                    var current = initial;

                    for (0..bst.len) |_| {
                        var next_result = current.next(start_key);

                        switch (next_result) {
                            .left => |left_node| {
                                // Only store larger keys
                                try parent_stack.append(allocator, current);
                                current = left_node;
                            },
                            .left_null => {
                                break; // there's no smaller key
                            },
                            .right => |right_node| {
                                current = right_node;
                            },
                            .right_null => {
                                // No larger values, stuck in local minima
                                current = parent_stack.pop().?;
                                break;
                            },
                            .end => {
                                break;
                            },
                        }
                    }

                    return .{
                        .parent_stack = parent_stack,
                        .current = current,
                    };
                } else {
                    @panic("empty");
                }
            }

            pub fn deinit(self: *@This(), allocator: std.mem.Allocator) void {
                self.parent_stack.deinit(allocator);
            }

            fn next_node(self: *@This()) ?*Node {
                if (self.current) |current| {
                    if (current.right == null) {
                        if (self.parent_stack.popOrNull()) |result| {
                            return result;
                        }
                        return null;
                    }
                    return current.right;
                }
                return null;
            }

            pub fn next(self: *@This()) ?Entry {
                var result: ?Entry = null;
                if (self.current) |current| {
                    result = .{
                        .key_ptr = &current.key,
                        .value_ptr = &current.val,
                    };
                }

                if (self.next_node()) |n| {
                    self.current = n;
                }

                return result;
            }
        };

        pub fn init(
            allocator: std.mem.Allocator,
            initial_size: usize,
        ) !@This() {
            return .{
                .root = null,
                .len = 0,
                .allocator = try std.heap.MemoryPool(Node).initPreheated(
                    allocator,
                    initial_size,
                ),
            };
        }

        pub fn deinit(self: *@This()) void {
            self.allocator.deinit();
        }

        pub fn put(
            self: *@This(),
            k: K,
            v: V,
        ) !void {
            var new_node = try self.allocator.create();
            new_node.* = .{ .key = k, .val = v, .left = null, .right = null };

            if (self.root) |root| {
                var current: *Node = root;
                for (0..self.len) |_| {
                    switch (current.next(k)) {
                        .left => |left_node| current = left_node,
                        .right => |right_node| current = right_node,
                        .left_null => {
                            current.*.left = new_node;
                            break;
                        },
                        .right_null => {
                            current.*.right = new_node;
                            break;
                        },
                        .end => {
                            std.debug.panic(
                                "Attempted to add duplicate key {}",
                                .{k},
                            );
                        },
                    }
                }
            } else {
                // Tree is empty!
                self.root = new_node;
            }

            self.len += 1;
        }

        // Can be used for prefix scans
        fn find_node(self: @This(), k: K) FoundNode {
            var result: FoundNode = .{ .not_found = {} };

            if (self.root) |root| {
                var current: *Node = root;

                for (0..self.len) |_| {
                    switch (current.next(k)) {
                        .left => |left_node| {
                            result = .{ .first_gt = current };
                            current = left_node;
                        },
                        .left_null => {
                            result = .{ .first_gt = current };
                            break; // there's no smaller key
                        },
                        .right => |right_node| current = right_node,
                        .right_null => break, // there's no larger key
                        .end => {
                            result = .{ .eq = current };
                            break;
                        },
                    }
                }
            }

            return result;
        }

        pub fn get(self: @This(), k: K) ?V {
            var result: ?V = null;
            if (self.root) |root| {
                var current: *Node = root;

                for (0..self.len) |_| {
                    switch (current.next(k)) {
                        .left => |left_node| current = left_node,
                        .left_null => break, // there's no smaller key
                        .right => |right_node| current = right_node,
                        .right_null => break, // there's no larger key
                        .end => {
                            result = current.val;
                            break;
                        },
                    }
                }
            }

            return result;
        }

        pub fn iterator(
            self: @This(),
            allocator: std.mem.Allocator,
        ) !Iterator {
            return try Iterator.init(allocator, self);
        }

        pub fn iteratorFrom(
            self: @This(),
            allocator: std.mem.Allocator,
            k: K,
        ) !Iterator {
            // TODO: better 'length' heurestic than the number of nodes in tree
            // This will over allocate
            return try Iterator.initFrom(allocator, self, k);
        }
    };
}
