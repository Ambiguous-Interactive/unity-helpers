# 2D Spatial Tree Performance Benchmarks

## TL;DR: What Problem This Solves

- Fast range/bounds/nearest‑neighbor queries on 2D data without scanning everything.
- Quick picks: QuadTree2D for broad‑phase; KdTree2D (Balanced) for NN; KdTree2D (Unbalanced) for fast rebuilds; RTree2D for bounds‑based data.

This document contains performance benchmarks for the 2D spatial tree implementations in Unity Helpers.

## Available 2D Spatial Trees

- **QuadTree2D** - Easiest to use, good all-around performance
- **KdTree2D** - Balanced and unbalanced variants available
- **RTree2D** - Optimized for bounding box queries

### Correctness & Semantics

- QuadTree2D and KdTree2D (balanced and unbalanced) guarantee the same results for the same input data and the same queries. They are both point-based trees and differ only in construction/query performance characteristics.
- RTree2D is bounds-based (stores rectangles/AABBs), not points. Its spatial knowledge and query semantics operate on rectangles, so its results will intentionally differ for sized objects and bounds intersection queries.

## Performance Benchmarks

<!-- SPATIAL_TREE_BENCHMARKS_START -->

### Datasets

<!-- tabs:start -->

#### **1,000,000 entries**

##### Construction

<table data-sortable>
  <thead>
    <tr>
      <th align="left">Construction</th>
      <th align="right">KDTree2D (Balanced)</th>
      <th align="right">KDTree2D (Unbalanced)</th>
      <th align="right">QuadTree2D</th>
      <th align="right">RTree2D</th>
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">1,000,000 entries</td><td align="right">2 (0.339s)</td><td align="right">6 (0.163s)</td><td align="right">4 (0.225s)</td><td align="right">3 (0.274s)</td></tr>
  </tbody>
</table>

##### Elements In Range

<table data-sortable>
  <thead>
    <tr>
      <th align="left">Elements In Range</th>
      <th align="right">KDTree2D (Balanced)</th>
      <th align="right">KDTree2D (Unbalanced)</th>
      <th align="right">QuadTree2D</th>
      <th align="right">RTree2D</th>
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">Full (~span/2) (r=499.5)</td><td align="right">59</td><td align="right">59</td><td align="right">55</td><td align="right">7</td></tr>
    <tr><td align="left">Half (~span/4) (r=249.8)</td><td align="right">238</td><td align="right">231</td><td align="right">204</td><td align="right">28</td></tr>
    <tr><td align="left">Quarter (~span/8) (r=124.9)</td><td align="right">945</td><td align="right">927</td><td align="right">813</td><td align="right">119</td></tr>
    <tr><td align="left">Tiny (~span/1000) (r=1)</td><td align="right">98,293</td><td align="right">99,231</td><td align="right">136,859</td><td align="right">98,699</td></tr>
  </tbody>
</table>

##### Get Elements In Bounds

<table data-sortable>
  <thead>
    <tr>
      <th align="left">Get Elements In Bounds</th>
      <th align="right">KDTree2D (Balanced)</th>
      <th align="right">KDTree2D (Unbalanced)</th>
      <th align="right">QuadTree2D</th>
      <th align="right">RTree2D</th>
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">Full (size=999.0x999.0)</td><td align="right">270</td><td align="right">394</td><td align="right">359</td><td align="right">17</td></tr>
    <tr><td align="left">Half (size=499.5x499.5)</td><td align="right">1,774</td><td align="right">1,812</td><td align="right">1,224</td><td align="right">69</td></tr>
    <tr><td align="left">Quarter (size=249.8x249.8)</td><td align="right">6,874</td><td align="right">7,157</td><td align="right">3,820</td><td align="right">361</td></tr>
    <tr><td align="left">Unit (size=1)</td><td align="right">141,582</td><td align="right">145,100</td><td align="right">184,680</td><td align="right">101,563</td></tr>
  </tbody>
</table>

##### Approximate Nearest Neighbors

<table data-sortable>
  <thead>
    <tr>
      <th align="left">Approximate Nearest Neighbors</th>
      <th align="right">KDTree2D (Balanced)</th>
      <th align="right">KDTree2D (Unbalanced)</th>
      <th align="right">QuadTree2D</th>
      <th align="right">RTree2D</th>
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">500 neighbors</td><td align="right">8,170</td><td align="right">16,230</td><td align="right">12,171</td><td align="right">59,089</td></tr>
    <tr><td align="left">100 neighbors</td><td align="right">67,998</td><td align="right">66,044</td><td align="right">66,325</td><td align="right">123,678</td></tr>
    <tr><td align="left">10 neighbors</td><td align="right">203,190</td><td align="right">195,156</td><td align="right">145,400</td><td align="right">170,276</td></tr>
    <tr><td align="left">1 neighbor</td><td align="right">263,680</td><td align="right">261,576</td><td align="right">143,886</td><td align="right">176,123</td></tr>
  </tbody>
</table>

#### **100,000 entries**

##### Construction

<table data-sortable>
  <thead>
    <tr>
      <th align="left">Construction</th>
      <th align="right">KDTree2D (Balanced)</th>
      <th align="right">KDTree2D (Unbalanced)</th>
      <th align="right">QuadTree2D</th>
      <th align="right">RTree2D</th>
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">100,000 entries</td><td align="right">49 (0.020s)</td><td align="right">84 (0.012s)</td><td align="right">49 (0.020s)</td><td align="right">50 (0.020s)</td></tr>
  </tbody>
</table>

##### Elements In Range

<table data-sortable>
  <thead>
    <tr>
      <th align="left">Elements In Range</th>
      <th align="right">KDTree2D (Balanced)</th>
      <th align="right">KDTree2D (Unbalanced)</th>
      <th align="right">QuadTree2D</th>
      <th align="right">RTree2D</th>
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">Full (~span/2) (r=199.5)</td><td align="right">600</td><td align="right">601</td><td align="right">585</td><td align="right">74</td></tr>
    <tr><td align="left">Half (~span/4) (r=99.75)</td><td align="right">1,354</td><td align="right">1,324</td><td align="right">1,202</td><td align="right">185</td></tr>
    <tr><td align="left">Quarter (~span/8) (r=49.88)</td><td align="right">4,641</td><td align="right">5,036</td><td align="right">4,274</td><td align="right">721</td></tr>
    <tr><td align="left">Tiny (~span/1000) (r=1)</td><td align="right">120,657</td><td align="right">120,129</td><td align="right">168,546</td><td align="right">130,169</td></tr>
  </tbody>
</table>

##### Get Elements In Bounds

<table data-sortable>
  <thead>
    <tr>
      <th align="left">Get Elements In Bounds</th>
      <th align="right">KDTree2D (Balanced)</th>
      <th align="right">KDTree2D (Unbalanced)</th>
      <th align="right">QuadTree2D</th>
      <th align="right">RTree2D</th>
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">Full (size=399.0x249.0)</td><td align="right">4,494</td><td align="right">4,508</td><td align="right">4,609</td><td align="right">236</td></tr>
    <tr><td align="left">Half (size=199.5x124.5)</td><td align="right">9,417</td><td align="right">11,739</td><td align="right">7,955</td><td align="right">957</td></tr>
    <tr><td align="left">Quarter (size=99.75x62.25)</td><td align="right">25,018</td><td align="right">31,756</td><td align="right">19,444</td><td align="right">3,787</td></tr>
    <tr><td align="left">Unit (size=1)</td><td align="right">172,229</td><td align="right">173,621</td><td align="right">226,206</td><td align="right">136,957</td></tr>
  </tbody>
</table>

##### Approximate Nearest Neighbors

<table data-sortable>
  <thead>
    <tr>
      <th align="left">Approximate Nearest Neighbors</th>
      <th align="right">KDTree2D (Balanced)</th>
      <th align="right">KDTree2D (Unbalanced)</th>
      <th align="right">QuadTree2D</th>
      <th align="right">RTree2D</th>
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">500 neighbors</td><td align="right">9,746</td><td align="right">9,649</td><td align="right">11,279</td><td align="right">59,259</td></tr>
    <tr><td align="left">100 neighbors</td><td align="right">45,035</td><td align="right">78,375</td><td align="right">48,623</td><td align="right">141,809</td></tr>
    <tr><td align="left">10 neighbors</td><td align="right">224,805</td><td align="right">201,634</td><td align="right">161,243</td><td align="right">191,622</td></tr>
    <tr><td align="left">1 neighbor</td><td align="right">204,442</td><td align="right">272,235</td><td align="right">186,148</td><td align="right">199,245</td></tr>
  </tbody>
</table>

#### **10,000 entries**

##### Construction

<table data-sortable>
  <thead>
    <tr>
      <th align="left">Construction</th>
      <th align="right">KDTree2D (Balanced)</th>
      <th align="right">KDTree2D (Unbalanced)</th>
      <th align="right">QuadTree2D</th>
      <th align="right">RTree2D</th>
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">10,000 entries</td><td align="right">547 (0.002s)</td><td align="right">205 (0.005s)</td><td align="right">534 (0.002s)</td><td align="right">510 (0.002s)</td></tr>
  </tbody>
</table>

##### Elements In Range

<table data-sortable>
  <thead>
    <tr>
      <th align="left">Elements In Range</th>
      <th align="right">KDTree2D (Balanced)</th>
      <th align="right">KDTree2D (Unbalanced)</th>
      <th align="right">QuadTree2D</th>
      <th align="right">RTree2D</th>
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">Full (~span/2) (r=49.50)</td><td align="right">5,835</td><td align="right">5,923</td><td align="right">5,910</td><td align="right">716</td></tr>
    <tr><td align="left">Half (~span/4) (r=24.75)</td><td align="right">22,309</td><td align="right">22,365</td><td align="right">13,492</td><td align="right">2,851</td></tr>
    <tr><td align="left">Quarter (~span/8) (r=12.38)</td><td align="right">43,600</td><td align="right">50,531</td><td align="right">36,923</td><td align="right">11,864</td></tr>
    <tr><td align="left">Tiny (~span/1000) (r=1)</td><td align="right">158,469</td><td align="right">150,623</td><td align="right">212,018</td><td align="right">149,034</td></tr>
  </tbody>
</table>

##### Get Elements In Bounds

<table data-sortable>
  <thead>
    <tr>
      <th align="left">Get Elements In Bounds</th>
      <th align="right">KDTree2D (Balanced)</th>
      <th align="right">KDTree2D (Unbalanced)</th>
      <th align="right">QuadTree2D</th>
      <th align="right">RTree2D</th>
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">Full (size=99.00x99.00)</td><td align="right">44,427</td><td align="right">44,160</td><td align="right">45,629</td><td align="right">2,400</td></tr>
    <tr><td align="left">Half (size=49.50x49.50)</td><td align="right">138,733</td><td align="right">137,563</td><td align="right">36,764</td><td align="right">9,208</td></tr>
    <tr><td align="left">Quarter (size=24.75x24.75)</td><td align="right">71,422</td><td align="right">99,969</td><td align="right">73,188</td><td align="right">34,689</td></tr>
    <tr><td align="left">Unit (size=1)</td><td align="right">217,195</td><td align="right">215,448</td><td align="right">288,202</td><td align="right">161,586</td></tr>
  </tbody>
</table>

##### Approximate Nearest Neighbors

<table data-sortable>
  <thead>
    <tr>
      <th align="left">Approximate Nearest Neighbors</th>
      <th align="right">KDTree2D (Balanced)</th>
      <th align="right">KDTree2D (Unbalanced)</th>
      <th align="right">QuadTree2D</th>
      <th align="right">RTree2D</th>
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">500 neighbors</td><td align="right">12,737</td><td align="right">12,616</td><td align="right">13,946</td><td align="right">55,527</td></tr>
    <tr><td align="left">100 neighbors</td><td align="right">55,274</td><td align="right">49,313</td><td align="right">78,099</td><td align="right">148,808</td></tr>
    <tr><td align="left">10 neighbors</td><td align="right">227,411</td><td align="right">158,790</td><td align="right">169,058</td><td align="right">209,840</td></tr>
    <tr><td align="left">1 neighbor</td><td align="right">223,606</td><td align="right">265,422</td><td align="right">200,809</td><td align="right">221,366</td></tr>
  </tbody>
</table>

#### **1,000 entries**

##### Construction

<table data-sortable>
  <thead>
    <tr>
      <th align="left">Construction</th>
      <th align="right">KDTree2D (Balanced)</th>
      <th align="right">KDTree2D (Unbalanced)</th>
      <th align="right">QuadTree2D</th>
      <th align="right">RTree2D</th>
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">1,000 entries</td><td align="right">5,151 (0.000s)</td><td align="right">7,776 (0.000s)</td><td align="right">4,694 (0.000s)</td><td align="right">4,555 (0.000s)</td></tr>
  </tbody>
</table>

##### Elements In Range

<table data-sortable>
  <thead>
    <tr>
      <th align="left">Elements In Range</th>
      <th align="right">KDTree2D (Balanced)</th>
      <th align="right">KDTree2D (Unbalanced)</th>
      <th align="right">QuadTree2D</th>
      <th align="right">RTree2D</th>
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">Full (~span/2) (r=24.50)</td><td align="right">56,995</td><td align="right">56,230</td><td align="right">55,896</td><td align="right">7,058</td></tr>
    <tr><td align="left">Half (~span/4) (r=12.25)</td><td align="right">59,112</td><td align="right">74,559</td><td align="right">55,979</td><td align="right">13,922</td></tr>
    <tr><td align="left">Quarter (~span/8) (r=6.13)</td><td align="right">92,658</td><td align="right">104,920</td><td align="right">92,486</td><td align="right">35,680</td></tr>
    <tr><td align="left">Tiny (~span/1000) (r=1)</td><td align="right">222,202</td><td align="right">220,277</td><td align="right">297,368</td><td align="right">206,152</td></tr>
  </tbody>
</table>

##### Get Elements In Bounds

<table data-sortable>
  <thead>
    <tr>
      <th align="left">Get Elements In Bounds</th>
      <th align="right">KDTree2D (Balanced)</th>
      <th align="right">KDTree2D (Unbalanced)</th>
      <th align="right">QuadTree2D</th>
      <th align="right">RTree2D</th>
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">Full (size=49.00x19.00)</td><td align="right">426,624</td><td align="right">431,008</td><td align="right">373,813</td><td align="right">23,621</td></tr>
    <tr><td align="left">Half (size=24.50x9.5)</td><td align="right">156,916</td><td align="right">256,359</td><td align="right">117,824</td><td align="right">71,241</td></tr>
    <tr><td align="left">Quarter (size=12.25x4.75)</td><td align="right">246,321</td><td align="right">255,452</td><td align="right">181,493</td><td align="right">157,622</td></tr>
    <tr><td align="left">Unit (size=1)</td><td align="right">299,240</td><td align="right">298,725</td><td align="right">399,346</td><td align="right">236,769</td></tr>
  </tbody>
</table>

##### Approximate Nearest Neighbors

<table data-sortable>
  <thead>
    <tr>
      <th align="left">Approximate Nearest Neighbors</th>
      <th align="right">KDTree2D (Balanced)</th>
      <th align="right">KDTree2D (Unbalanced)</th>
      <th align="right">QuadTree2D</th>
      <th align="right">RTree2D</th>
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">500 neighbors</td><td align="right">41,173</td><td align="right">42,778</td><td align="right">36,594</td><td align="right">59,730</td></tr>
    <tr><td align="left">100 neighbors</td><td align="right">68,908</td><td align="right">67,186</td><td align="right">74,762</td><td align="right">162,066</td></tr>
    <tr><td align="left">10 neighbors</td><td align="right">229,540</td><td align="right">233,936</td><td align="right">192,974</td><td align="right">227,546</td></tr>
    <tr><td align="left">1 neighbor</td><td align="right">306,128</td><td align="right">230,085</td><td align="right">193,384</td><td align="right">228,793</td></tr>
  </tbody>
</table>

#### **100 entries**

##### Construction

<table data-sortable>
  <thead>
    <tr>
      <th align="left">Construction</th>
      <th align="right">KDTree2D (Balanced)</th>
      <th align="right">KDTree2D (Unbalanced)</th>
      <th align="right">QuadTree2D</th>
      <th align="right">RTree2D</th>
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">100 entries</td><td align="right">39,370 (0.000s)</td><td align="right">22,675 (0.000s)</td><td align="right">14,641 (0.000s)</td><td align="right">21,231 (0.000s)</td></tr>
  </tbody>
</table>

##### Elements In Range

<table data-sortable>
  <thead>
    <tr>
      <th align="left">Elements In Range</th>
      <th align="right">KDTree2D (Balanced)</th>
      <th align="right">KDTree2D (Unbalanced)</th>
      <th align="right">QuadTree2D</th>
      <th align="right">RTree2D</th>
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">Full (~span/2) (r=4.5)</td><td align="right">424,908</td><td align="right">350,023</td><td align="right">429,776</td><td align="right">68,536</td></tr>
    <tr><td align="left">Half (~span/4) (r=2.25)</td><td align="right">368,298</td><td align="right">327,663</td><td align="right">232,135</td><td align="right">198,728</td></tr>
    <tr><td align="left">Quarter (~span/8) (r=1.13)</td><td align="right">361,799</td><td align="right">333,252</td><td align="right">484,465</td><td align="right">270,330</td></tr>
    <tr><td align="left">Tiny (~span/1000) (r=1)</td><td align="right">366,765</td><td align="right">339,049</td><td align="right">491,007</td><td align="right">271,447</td></tr>
  </tbody>
</table>

##### Get Elements In Bounds

<table data-sortable>
  <thead>
    <tr>
      <th align="left">Get Elements In Bounds</th>
      <th align="right">KDTree2D (Balanced)</th>
      <th align="right">KDTree2D (Unbalanced)</th>
      <th align="right">QuadTree2D</th>
      <th align="right">RTree2D</th>
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">Full (size=9x9)</td><td align="right">1,213,305</td><td align="right">1,199,091</td><td align="right">1,381,012</td><td align="right">193,676</td></tr>
    <tr><td align="left">Half (size=4.5x4.5)</td><td align="right">366,200</td><td align="right">390,340</td><td align="right">324,182</td><td align="right">296,281</td></tr>
    <tr><td align="left">Quarter (size=2.25x2.25)</td><td align="right">389,918</td><td align="right">419,365</td><td align="right">622,169</td><td align="right">304,900</td></tr>
    <tr><td align="left">Unit (size=1)</td><td align="right">377,223</td><td align="right">449,661</td><td align="right">606,231</td><td align="right">303,985</td></tr>
  </tbody>
</table>

##### Approximate Nearest Neighbors

<table data-sortable>
  <thead>
    <tr>
      <th align="left">Approximate Nearest Neighbors</th>
      <th align="right">KDTree2D (Balanced)</th>
      <th align="right">KDTree2D (Unbalanced)</th>
      <th align="right">QuadTree2D</th>
      <th align="right">RTree2D</th>
    </tr>
  </thead>
  <tbody>
    <tr><td align="left">100 neighbors (max)</td><td align="right">85,200</td><td align="right">110,718</td><td align="right">130,989</td><td align="right">171,955</td></tr>
    <tr><td align="left">10 neighbors</td><td align="right">175,016</td><td align="right">222,050</td><td align="right">233,907</td><td align="right">270,041</td></tr>
    <tr><td align="left">1 neighbor</td><td align="right">178,877</td><td align="right">289,379</td><td align="right">231,762</td><td align="right">284,316</td></tr>
  </tbody>
</table>
<!-- tabs:end -->
<!-- SPATIAL_TREE_BENCHMARKS_END -->

## Interpreting the Results

All numbers represent **operations per second** (higher is better), except for construction times which show operations per second and absolute time.

### Choosing the Right Tree

**QuadTree2D**:

- Best for: General-purpose 2D spatial queries
- Strengths: Balanced performance across all operation types, simple to use
- Weaknesses: Slightly slower than KdTree for point queries

**KdTree2D (Balanced)**:

- Best for: When you need consistent query performance
- Strengths: Fast nearest-neighbor queries, good for smaller datasets
- Weaknesses: Slower construction time

**KdTree2D (Unbalanced)**:

- Best for: When you need fast construction and will rebuild frequently
- Strengths: Fastest construction, similar query performance to balanced
- Weaknesses: May degrade on pathological data distributions

**RTree2D**:

- Best for: Bounding box queries, especially with large query areas
- Strengths: Excellent for large bounding box queries, handles overlapping objects well
- Weaknesses: Slower for point queries and small ranges

### Important Notes

- All spatial trees assume **immutable** positional data
- If positions change, you must reconstruct the tree
- Spatial queries are O(log n) vs O(n) for linear search
- Construction cost is amortized over many queries
