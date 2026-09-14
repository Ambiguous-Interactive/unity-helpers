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
    <tr><td align="left">1,000,000 entries</td><td align="right">2 (0.362s)</td><td align="right">5 (0.195s)</td><td align="right">1 (0.745s)</td><td align="right">3 (0.313s)</td></tr>
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
    <tr><td align="left">Full (~span/2) (r=499.5)</td><td align="right">100</td><td align="right">98</td><td align="right">92</td><td align="right">16</td></tr>
    <tr><td align="left">Half (~span/4) (r=249.8)</td><td align="right">406</td><td align="right">409</td><td align="right">405</td><td align="right">78</td></tr>
    <tr><td align="left">Quarter (~span/8) (r=124.9)</td><td align="right">1,600</td><td align="right">1,594</td><td align="right">1,644</td><td align="right">344</td></tr>
    <tr><td align="left">Tiny (~span/1000) (r=1)</td><td align="right">161,731</td><td align="right">159,685</td><td align="right">237,950</td><td align="right">147,807</td></tr>
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
    <tr><td align="left">Full (size=999.0x999.0)</td><td align="right">315</td><td align="right">362</td><td align="right">328</td><td align="right">20</td></tr>
    <tr><td align="left">Half (size=499.5x499.5)</td><td align="right">1,746</td><td align="right">1,723</td><td align="right">1,772</td><td align="right">108</td></tr>
    <tr><td align="left">Quarter (size=249.8x249.8)</td><td align="right">6,787</td><td align="right">6,916</td><td align="right">6,997</td><td align="right">546</td></tr>
    <tr><td align="left">Unit (size=1)</td><td align="right">198,766</td><td align="right">194,827</td><td align="right">261,709</td><td align="right">152,855</td></tr>
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
    <tr><td align="left">500 neighbors</td><td align="right">16,775</td><td align="right">35,374</td><td align="right">26,298</td><td align="right">3,663</td></tr>
    <tr><td align="left">100 neighbors</td><td align="right">161,271</td><td align="right">131,713</td><td align="right">147,749</td><td align="right">18,238</td></tr>
    <tr><td align="left">10 neighbors</td><td align="right">506,799</td><td align="right">494,356</td><td align="right">261,774</td><td align="right">29,127</td></tr>
    <tr><td align="left">1 neighbor</td><td align="right">618,345</td><td align="right">606,530</td><td align="right">272,364</td><td align="right">29,740</td></tr>
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
    <tr><td align="left">100,000 entries</td><td align="right">44 (0.023s)</td><td align="right">67 (0.015s)</td><td align="right">15 (0.064s)</td><td align="right">37 (0.026s)</td></tr>
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
    <tr><td align="left">Full (~span/2) (r=199.5)</td><td align="right">1,020</td><td align="right">1,004</td><td align="right">1,021</td><td align="right">215</td></tr>
    <tr><td align="left">Half (~span/4) (r=99.75)</td><td align="right">2,281</td><td align="right">2,306</td><td align="right">2,341</td><td align="right">538</td></tr>
    <tr><td align="left">Quarter (~span/8) (r=49.88)</td><td align="right">7,804</td><td align="right">8,704</td><td align="right">9,376</td><td align="right">2,105</td></tr>
    <tr><td align="left">Tiny (~span/1000) (r=1)</td><td align="right">195,261</td><td align="right">196,197</td><td align="right">279,042</td><td align="right">194,307</td></tr>
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
    <tr><td align="left">Full (size=399.0x249.0)</td><td align="right">4,482</td><td align="right">4,379</td><td align="right">4,487</td><td align="right">341</td></tr>
    <tr><td align="left">Half (size=199.5x124.5)</td><td align="right">11,228</td><td align="right">12,982</td><td align="right">14,742</td><td align="right">1,412</td></tr>
    <tr><td align="left">Quarter (size=99.75x62.25)</td><td align="right">31,186</td><td align="right">37,446</td><td align="right">43,419</td><td align="right">5,631</td></tr>
    <tr><td align="left">Unit (size=1)</td><td align="right">228,484</td><td align="right">225,721</td><td align="right">315,803</td><td align="right">205,613</td></tr>
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
    <tr><td align="left">500 neighbors</td><td align="right">23,576</td><td align="right">22,768</td><td align="right">24,282</td><td align="right">5,045</td></tr>
    <tr><td align="left">100 neighbors</td><td align="right">107,485</td><td align="right">190,998</td><td align="right">99,608</td><td align="right">17,425</td></tr>
    <tr><td align="left">10 neighbors</td><td align="right">481,852</td><td align="right">501,441</td><td align="right">292,286</td><td align="right">40,726</td></tr>
    <tr><td align="left">1 neighbor</td><td align="right">596,868</td><td align="right">616,172</td><td align="right">301,254</td><td align="right">43,377</td></tr>
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
    <tr><td align="left">10,000 entries</td><td align="right">491 (0.002s)</td><td align="right">669 (0.001s)</td><td align="right">202 (0.005s)</td><td align="right">413 (0.002s)</td></tr>
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
    <tr><td align="left">Full (~span/2) (r=49.50)</td><td align="right">10,059</td><td align="right">10,063</td><td align="right">10,024</td><td align="right">2,149</td></tr>
    <tr><td align="left">Half (~span/4) (r=24.75)</td><td align="right">38,194</td><td align="right">37,816</td><td align="right">39,638</td><td align="right">8,431</td></tr>
    <tr><td align="left">Quarter (~span/8) (r=12.38)</td><td align="right">70,930</td><td align="right">83,939</td><td align="right">99,255</td><td align="right">33,540</td></tr>
    <tr><td align="left">Tiny (~span/1000) (r=1)</td><td align="right">247,372</td><td align="right">246,625</td><td align="right">341,658</td><td align="right">226,037</td></tr>
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
    <tr><td align="left">Full (size=99.00x99.00)</td><td align="right">44,817</td><td align="right">44,113</td><td align="right">44,394</td><td align="right">3,536</td></tr>
    <tr><td align="left">Half (size=49.50x49.50)</td><td align="right">164,390</td><td align="right">168,864</td><td align="right">172,050</td><td align="right">13,410</td></tr>
    <tr><td align="left">Quarter (size=24.75x24.75)</td><td align="right">98,908</td><td align="right">135,348</td><td align="right">171,080</td><td align="right">50,695</td></tr>
    <tr><td align="left">Unit (size=1)</td><td align="right">292,361</td><td align="right">283,248</td><td align="right">379,910</td><td align="right">236,640</td></tr>
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
    <tr><td align="left">500 neighbors</td><td align="right">30,990</td><td align="right">30,435</td><td align="right">29,942</td><td align="right">5,229</td></tr>
    <tr><td align="left">100 neighbors</td><td align="right">135,108</td><td align="right">123,954</td><td align="right">159,352</td><td align="right">23,612</td></tr>
    <tr><td align="left">10 neighbors</td><td align="right">495,078</td><td align="right">493,259</td><td align="right">327,237</td><td align="right">54,246</td></tr>
    <tr><td align="left">1 neighbor</td><td align="right">631,140</td><td align="right">530,323</td><td align="right">390,299</td><td align="right">61,864</td></tr>
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
    <tr><td align="left">1,000 entries</td><td align="right">4,416 (0.000s)</td><td align="right">6,591 (0.000s)</td><td align="right">1,976 (0.001s)</td><td align="right">3,907 (0.000s)</td></tr>
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
    <tr><td align="left">Full (~span/2) (r=24.50)</td><td align="right">95,715</td><td align="right">97,169</td><td align="right">97,918</td><td align="right">21,417</td></tr>
    <tr><td align="left">Half (~span/4) (r=12.25)</td><td align="right">94,861</td><td align="right">123,468</td><td align="right">119,436</td><td align="right">40,197</td></tr>
    <tr><td align="left">Quarter (~span/8) (r=6.13)</td><td align="right">147,619</td><td align="right">169,956</td><td align="right">181,777</td><td align="right">84,623</td></tr>
    <tr><td align="left">Tiny (~span/1000) (r=1)</td><td align="right">346,917</td><td align="right">348,862</td><td align="right">458,277</td><td align="right">320,193</td></tr>
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
    <tr><td align="left">Full (size=49.00x19.00)</td><td align="right">432,109</td><td align="right">441,180</td><td align="right">465,708</td><td align="right">35,018</td></tr>
    <tr><td align="left">Half (size=24.50x9.5)</td><td align="right">209,301</td><td align="right">357,349</td><td align="right">363,098</td><td align="right">105,115</td></tr>
    <tr><td align="left">Quarter (size=12.25x4.75)</td><td align="right">334,684</td><td align="right">364,976</td><td align="right">466,838</td><td align="right">228,618</td></tr>
    <tr><td align="left">Unit (size=1)</td><td align="right">404,675</td><td align="right">390,143</td><td align="right">502,011</td><td align="right">337,746</td></tr>
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
    <tr><td align="left">500 neighbors</td><td align="right">38,041</td><td align="right">41,051</td><td align="right">39,006</td><td align="right">5,841</td></tr>
    <tr><td align="left">100 neighbors</td><td align="right">156,877</td><td align="right">152,010</td><td align="right">160,805</td><td align="right">24,720</td></tr>
    <tr><td align="left">10 neighbors</td><td align="right">568,521</td><td align="right">609,294</td><td align="right">405,817</td><td align="right">92,332</td></tr>
    <tr><td align="left">1 neighbor</td><td align="right">531,965</td><td align="right">641,229</td><td align="right">334,066</td><td align="right">106,525</td></tr>
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
    <tr><td align="left">100 entries</td><td align="right">38,314 (0.000s)</td><td align="right">37,593 (0.000s)</td><td align="right">18,315 (0.000s)</td><td align="right">18,382 (0.000s)</td></tr>
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
    <tr><td align="left">Full (~span/2) (r=4.5)</td><td align="right">707,466</td><td align="right">707,344</td><td align="right">698,430</td><td align="right">190,481</td></tr>
    <tr><td align="left">Half (~span/4) (r=2.25)</td><td align="right">579,526</td><td align="right">574,963</td><td align="right">734,467</td><td align="right">379,167</td></tr>
    <tr><td align="left">Quarter (~span/8) (r=1.13)</td><td align="right">580,850</td><td align="right">585,912</td><td align="right">742,405</td><td align="right">427,445</td></tr>
    <tr><td align="left">Tiny (~span/1000) (r=1)</td><td align="right">576,125</td><td align="right">583,627</td><td align="right">744,381</td><td align="right">429,480</td></tr>
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
    <tr><td align="left">Full (size=9x9)</td><td align="right">1,564,474</td><td align="right">1,567,597</td><td align="right">1,584,871</td><td align="right">282,253</td></tr>
    <tr><td align="left">Half (size=4.5x4.5)</td><td align="right">641,201</td><td align="right">644,062</td><td align="right">792,659</td><td align="right">419,895</td></tr>
    <tr><td align="left">Quarter (size=2.25x2.25)</td><td align="right">650,761</td><td align="right">653,857</td><td align="right">788,861</td><td align="right">438,508</td></tr>
    <tr><td align="left">Unit (size=1)</td><td align="right">647,476</td><td align="right">664,227</td><td align="right">787,188</td><td align="right">436,589</td></tr>
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
    <tr><td align="left">100 neighbors (max)</td><td align="right">191,767</td><td align="right">191,405</td><td align="right">180,671</td><td align="right">154,573</td></tr>
    <tr><td align="left">10 neighbors</td><td align="right">640,162</td><td align="right">540,897</td><td align="right">472,754</td><td align="right">265,960</td></tr>
    <tr><td align="left">1 neighbor</td><td align="right">658,223</td><td align="right">565,971</td><td align="right">517,711</td><td align="right">350,069</td></tr>
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
